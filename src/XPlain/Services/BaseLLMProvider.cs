using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public abstract class BaseLLMProvider : ILLMProvider
    {
        protected readonly ILogger _logger;
        protected readonly CircuitBreaker _circuitBreaker;
        protected readonly HttpClient _httpClient;
        protected readonly IRateLimitingService _rateLimitingService;
        protected readonly LLMProviderMetrics _metrics;
        protected readonly TimeSpan _timeout;

        protected BaseLLMProvider(
            ILogger logger,
            HttpClient httpClient,
            IRateLimitingService rateLimitingService,
            LLMProviderMetrics metrics,
            IOptions<LLMSettings> settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _rateLimitingService = rateLimitingService ?? throw new ArgumentNullException(nameof(rateLimitingService));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _timeout = TimeSpan.FromSeconds(settings?.Value?.TimeoutSeconds ?? 30);
            
            _circuitBreaker = new CircuitBreaker(
                maxFailures: 3,
                resetTimeout: TimeSpan.FromMinutes(5));
        }

        public abstract string ProviderName { get; }
        public abstract string ModelName { get; }

        public virtual bool IsHealthy()
        {
            return _circuitBreaker.IsAllowed() && _rateLimitingService.CanMakeRequest(ProviderName);
        }

        public abstract Task<string> GetCompletionAsync(string prompt);

        protected virtual LLMProviderException ClassifyException(Exception ex)
        {
            return ex switch
            {
                HttpRequestException httpEx when httpEx.Message.Contains("401") =>
                    new LLMProviderException("Authentication failed", LLMErrorType.Unauthorized, false, httpEx),
                
                HttpRequestException httpEx when httpEx.Message.Contains("429") =>
                    new LLMProviderException("Rate limit exceeded", LLMErrorType.RateLimitExceeded, true, httpEx),
                
                TimeoutException timeoutEx =>
                    new LLMProviderException("Request timed out", LLMErrorType.Timeout, true, timeoutEx),
                
                OperationCanceledException cancelEx =>
                    new LLMProviderException("Request cancelled", LLMErrorType.Timeout, true, cancelEx),
                
                HttpRequestException httpEx when httpEx.Message.Contains("503") =>
                    new LLMProviderException("Service unavailable", LLMErrorType.ServiceUnavailable, true, httpEx),
                
                _ => new LLMProviderException($"Unknown error: {ex.Message}", LLMErrorType.Unknown, true, ex)
            };
        }

        protected async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            if (!_circuitBreaker.IsAllowed())
            {
                throw new InvalidOperationException($"Circuit breaker is open for provider {ProviderName}");
            }

            LLMProviderException lastException = null;
            var startTime = DateTime.UtcNow;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (!await _rateLimitingService.WaitForAvailabilityAsync(ProviderName))
                    {
                        throw new LLMProviderException(
                            "Rate limit exceeded", 
                            LLMErrorType.RateLimitExceeded, 
                            true);
                    }

                    using var cts = new CancellationTokenSource(_timeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    
                    var timeoutTask = Task.Delay(_timeout, linkedCts.Token);
                    var actionTask = action();
                    
                    var completedTask = await Task.WhenAny(actionTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        linkedCts.Cancel(); // Cancel the action task
                        throw new TimeoutException($"Request timed out after {_timeout.TotalSeconds} seconds");
                    }

                    linkedCts.Cancel(); // Cancel the timeout task
                    
                    if (actionTask.IsFaulted)
                    {
                        throw await actionTask;
                    }

                    var finalResult = await actionTask;
                    _circuitBreaker.OnSuccess();
                    _metrics.RecordSuccess(ProviderName, DateTime.UtcNow - startTime);
                    return finalResult;
                }
                catch (Exception ex) when (!(ex is LLMProviderException))
                {
                    lastException = ClassifyException(ex);
                    _metrics.RecordFailure(ProviderName);
                    _logger.LogWarning(ex, $"Attempt {attempt + 1} failed for {ProviderName}");
                    
                    if (attempt < maxRetries - 1 && lastException.IsTransient)
                    {
                        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(-500, 500)); // Add jitter
                        var finalDelay = baseDelay + jitter;
                        
                        _logger.LogInformation($"Waiting {finalDelay.TotalSeconds:F1}s before retry {attempt + 1} for {ProviderName}");
                        await Task.Delay(finalDelay);
                    }
                }
            }

            _circuitBreaker.OnFailure();
            _metrics.RecordFailure(ProviderName);
            throw lastException;
        }
    }
}