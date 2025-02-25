using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using XPlain.Services.Validation;

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
        protected readonly IInputValidator _inputValidator;

        // Original constructor with full parameters
        protected BaseLLMProvider(
            ILogger logger,
            HttpClient httpClient,
            IRateLimitingService rateLimitingService,
            LLMProviderMetrics metrics,
            IOptions<LLMSettings> settings,
            IInputValidator inputValidator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _rateLimitingService = rateLimitingService ?? throw new ArgumentNullException(nameof(rateLimitingService));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _timeout = TimeSpan.FromSeconds(settings?.Value?.TimeoutSeconds ?? 30);
            _inputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
            
            _circuitBreaker = new CircuitBreaker(
                maxFailures: 3,
                resetTimeout: TimeSpan.FromMinutes(5));
        }
        
        // Simplified constructor for backward compatibility
        protected BaseLLMProvider(
            ICacheProvider cacheProvider,
            IRateLimitingService rateLimitingService,
            IOptions<StreamingSettings> streamingSettings)
        {
            _logger = new Logger<BaseLLMProvider>(new LoggerFactory());
            
            // Configure HTTP client with streaming handler if available
            if (streamingSettings != null)
            {
                _httpClient = new HttpClient(new StreamingHttpHandler(streamingSettings.Value));
            }
            else
            {
                _httpClient = new HttpClient();
            }
            
            _rateLimitingService = rateLimitingService ?? throw new ArgumentNullException(nameof(rateLimitingService));
            _metrics = new LLMProviderMetrics();
            _timeout = TimeSpan.FromSeconds(30);
            _inputValidator = new DefaultInputValidator();
            
            _circuitBreaker = new CircuitBreaker(
                maxFailures: 3,
                resetTimeout: TimeSpan.FromMinutes(5));
        }

        // Default constructor for testing
        protected BaseLLMProvider()
        {
            _logger = new Logger<BaseLLMProvider>(new LoggerFactory());
            _httpClient = new HttpClient();
            _rateLimitingService = new RateLimitingService(
                Options.Create(new RateLimitSettings()), 
                new Logger<RateLimitingService>(new LoggerFactory()));
            _metrics = new LLMProviderMetrics();
            _timeout = TimeSpan.FromSeconds(30);
            _inputValidator = new DefaultInputValidator();
            
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

        public async Task<string> GetCompletionAsync(string prompt)
        {
            // Validate and sanitize the input before passing to the provider
            var validatedPrompt = await ValidateAndSanitizePromptAsync(prompt);
            return await GetCompletionInternalAsync(validatedPrompt);
        }
        


        protected abstract Task<string> GetCompletionInternalAsync(string validatedPrompt);
        
        protected virtual async IAsyncEnumerable<string> GetCompletionStreamInternalAsync(string validatedPrompt)
        {
            // Default implementation for providers that don't support streaming
            var response = await GetCompletionInternalAsync(validatedPrompt);
            yield return response;
        }
        
        public virtual async IAsyncEnumerable<string> GetCompletionStreamAsync(string prompt)
        {
            // Validate and sanitize the input before passing to the provider
            try
            {
                var validatedPrompt = await ValidateAndSanitizePromptAsync(prompt);
                await foreach (var chunk in GetCompletionStreamInternalAsync(validatedPrompt))
                {
                    yield return chunk;
                }
            }
            catch (Exception ex)
            {
                var llmEx = ex as LLMProviderException ?? ClassifyException(ex);
                _logger.LogError(llmEx, $"Error in streaming completion from {ProviderName}: {llmEx.Message}");
                
                // Return error message as a single chunk
                yield return $"Error: {llmEx.Message}";
            }
        }


        protected async Task<string> ValidateAndSanitizePromptAsync(string prompt)
        {
            try
            {
                return await _inputValidator.ValidateAndSanitizeAsync(prompt, ProviderName);
            }
            catch (InputValidationException ex)
            {
                _logger.LogWarning(ex, $"Input validation failed for provider {ProviderName}: {ex.ValidationError}");
                throw new LLMProviderException(
                    $"Input validation failed: {ex.Message}",
                    LLMErrorType.InvalidInput,
                    false,
                    ex);
            }
        }

        protected virtual LLMProviderException ClassifyException(Exception ex)
        {
            return ex switch
            {
                InputValidationException validationEx =>
                    new LLMProviderException(validationEx.Message, LLMErrorType.InvalidInput, false, validationEx),

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
                    bool available = await _rateLimitingService.WaitForAvailabilityAsync(ProviderName);
                    if (!available)
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