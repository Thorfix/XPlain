using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XPlain.Configuration;

namespace XPlain.Services
{
    public abstract class BaseLLMProvider : ILLMProvider
    {
        protected readonly ILogger _logger;
        protected readonly CircuitBreaker _circuitBreaker;
        protected readonly HttpClient _httpClient;
        protected readonly IRateLimitingService _rateLimitingService;

        protected BaseLLMProvider(
            ILogger logger,
            HttpClient httpClient,
            IRateLimitingService rateLimitingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _rateLimitingService = rateLimitingService ?? throw new ArgumentNullException(nameof(rateLimitingService));
            
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

        protected async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            if (!_circuitBreaker.IsAllowed())
            {
                throw new InvalidOperationException($"Circuit breaker is open for provider {ProviderName}");
            }

            Exception lastException = null;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    await _rateLimitingService.WaitForAvailabilityAsync(ProviderName);
                    var result = await action();
                    _circuitBreaker.OnSuccess();
                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, $"Attempt {attempt + 1} failed for {ProviderName}");
                    
                    if (attempt < maxRetries - 1)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                        await Task.Delay(delay);
                    }
                }
            }

            _circuitBreaker.OnFailure();
            throw new Exception($"All retry attempts failed for {ProviderName}", lastException);
        }
    }
}