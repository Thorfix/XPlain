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
        private readonly Dictionary<string, DateTime> _lastFailureTime = new();

        public FallbackLLMProvider(
            IEnumerable<ILLMProvider> providers,
            LLMFallbackSettings settings,
            ILogger<FallbackLLMProvider> logger)
        {
            _settings = settings;
            _logger = logger;
            _providers = providers.OrderBy(p => _settings.Providers.First(c => c.Name == p.GetType().Name).Priority).ToList();
            
            foreach (var provider in _providers)
            {
                _circuitBreakers[provider.GetType().Name] = new CircuitBreaker(
                    maxFailures: 3,
                    resetTimeout: TimeSpan.FromMinutes(5));
            }
        }

        public async Task<string> GetCompletion(string prompt)
        {
            foreach (var provider in _providers)
            {
                var providerName = provider.GetType().Name;
                var circuitBreaker = _circuitBreakers[providerName];

                if (!circuitBreaker.IsAllowed())
                {
                    _logger.LogWarning($"Provider {providerName} is currently in circuit breaker state");
                    continue;
                }

                try
                {
                    for (int attempt = 0; attempt < _settings.RetryAttempts; attempt++)
                    {
                        try
                        {
                            var result = await provider.GetCompletion(prompt);
                            circuitBreaker.OnSuccess();
                            return result;
                        }
                        catch (Exception ex) when (attempt < _settings.RetryAttempts - 1)
                        {
                            _logger.LogWarning(ex, $"Attempt {attempt + 1} failed for provider {providerName}");
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
                        }
                    }
                    
                    // If we get here, all retries failed
                    throw new Exception($"All retry attempts failed for provider {providerName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Provider {providerName} failed to process request");
                    circuitBreaker.OnFailure();
                    _lastFailureTime[providerName] = DateTime.UtcNow;
                }
            }

            throw new Exception("All LLM providers failed to process the request");
        }

        public bool IsHealthy()
        {
            return _providers.Any(p => _circuitBreakers[p.GetType().Name].IsAllowed());
        }
    }
}