using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class FallbackLLMProvider : ILLMProvider
    {
        private readonly List<ILLMProvider> _providers;
        private readonly ILogger<FallbackLLMProvider> _logger;
        private readonly LLMFallbackSettings _settings;
        private readonly Dictionary<string, CircuitBreaker> _circuitBreakers = new();
        private readonly LLMProviderMetrics _metrics;
        private readonly IRateLimitingService _rateLimitingService;

        public string ProviderName => "FallbackLLMProvider";
        public string ModelName => "Multiple";

        public FallbackLLMProvider(
            IEnumerable<ILLMProvider> providers,
            LLMFallbackSettings settings,
            ILogger<FallbackLLMProvider> logger,
            LLMProviderMetrics metrics,
            IRateLimitingService rateLimitingService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _rateLimitingService = rateLimitingService ?? throw new ArgumentNullException(nameof(rateLimitingService));
            
            _providers = providers
                .OrderBy(p => _settings.Providers.First(c => c.Name == p.GetType().Name).Priority)
                .ToList();

            foreach (var provider in _providers)
            {
                _circuitBreakers[provider.GetType().Name] = new CircuitBreaker(
                    maxFailures: 3,
                    resetTimeout: TimeSpan.FromMinutes(5));
            }
        }

        public bool IsHealthy()
        {
            return _providers.Any(p => 
            {
                var name = p.GetType().Name;
                return _circuitBreakers[name].IsAllowed() && 
                       _rateLimitingService.CanMakeRequest(name) &&
                       p.IsHealthy();
            });
        }

        public async Task<string> GetCompletionAsync(string prompt)
        {
            var errors = new List<LLMProviderException>();

            foreach (var provider in _providers)
            {
                var providerName = provider.GetType().Name;
                var circuitBreaker = _circuitBreakers[providerName];

                if (!circuitBreaker.IsAllowed())
                {
                    _logger.LogWarning($"Provider {providerName} is in circuit breaker state");
                    continue;
                }

                if (!await _rateLimitingService.WaitForAvailabilityAsync(providerName))
                {
                    _logger.LogWarning($"Provider {providerName} is rate limited");
                    continue;
                }

                if (!provider.IsHealthy())
                {
                    _logger.LogWarning($"Provider {providerName} reports unhealthy state");
                    continue;
                }

                try
                {
                    for (int attempt = 0; attempt < _settings.RetryAttempts; attempt++)
                    {
                        try
                        {
                            var startTime = DateTime.UtcNow;
                            var result = await provider.GetCompletionAsync(prompt);
                            
                            circuitBreaker.OnSuccess();
                            _metrics.RecordSuccess(providerName, DateTime.UtcNow - startTime);
                            
                            return result;
                        }
                        catch (Exception ex) when (attempt < _settings.RetryAttempts - 1)
                        {
                            var llmEx = ex as LLMProviderException ?? 
                                new LLMProviderException($"Unknown error: {ex.Message}", LLMErrorType.Unknown, true, ex);

                            if (!llmEx.IsTransient)
                            {
                                throw;
                            }

                            _logger.LogWarning(ex, $"Attempt {attempt + 1} failed for provider {providerName}");
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
                        }
                    }
                }
                catch (Exception ex)
                {
                    var llmEx = ex as LLMProviderException ?? 
                        new LLMProviderException($"Provider {providerName} failed: {ex.Message}", 
                            LLMErrorType.Unknown, true, ex);

                    errors.Add(llmEx);
                    _logger.LogError(llmEx, $"Provider {providerName} failed: {llmEx.ErrorType}");
                    _metrics.RecordFailure(providerName);
                    circuitBreaker.OnFailure();
                }
            }

            var errorMessage = string.Join("\n", errors.Select(e => $"- {e.Message} ({e.ErrorType})"));
            throw new LLMProviderException(
                $"All providers failed:\n{errorMessage}", 
                LLMErrorType.ServiceUnavailable, 
                true);
        }

        public Task<IAsyncEnumerable<string>> GetCompletionStreamAsync(string prompt)
        {
            throw new NotImplementedException("Streaming not implemented for fallback provider");
        }
    }
}