using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using XPlain.Services.Validation;

namespace XPlain.Services
{
    /// <summary>
    /// Factory class for creating LLM provider instances
    /// </summary>
    public class LLMProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly LLMSettings _llmSettings;
        private readonly LLMFallbackSettings _fallbackSettings;
        private readonly ILogger<LLMProviderFactory> _logger;
        private readonly IDictionary<string, IResponseValidator> _validators;

        public LLMProviderFactory(
            IServiceProvider serviceProvider,
            IOptions<LLMSettings> settings,
            IOptions<LLMFallbackSettings> fallbackSettings,
            ILogger<LLMProviderFactory> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _llmSettings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _fallbackSettings = fallbackSettings?.Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize validators
            _validators = new Dictionary<string, IResponseValidator>(StringComparer.OrdinalIgnoreCase)
            {
                { "anthropic", new AnthropicResponseValidator() },
                { "openai", new OpenAIResponseValidator() },
                { "azureopenai", new AzureOpenAIResponseValidator() }
            };
        }

        /// <summary>
        /// Gets the appropriate validator for the specified provider
        /// </summary>
        private IResponseValidator GetValidator(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
            {
                throw new ArgumentException("Provider name cannot be empty", nameof(providerName));
            }

            if (!_validators.TryGetValue(providerName, out var validator))
            {
                throw new ArgumentException($"No validator found for provider: {providerName}");
            }

            return validator;
        }

        /// <summary>
        /// Validates the response from an LLM provider
        /// </summary>
        public async Task ValidateResponseAsync(string providerName, string response)
        {
            var validator = GetValidator(providerName);

            if (!await validator.ValidateSchemaAsync(response))
                throw new ResponseValidationException($"Invalid response schema from {providerName}", ResponseValidationType.Schema);

            if (!await validator.ValidateQualityAsync(response))
                throw new ResponseValidationException($"Response quality check failed for {providerName}", ResponseValidationType.Quality);

            if (!await validator.ValidateFormatAsync(response))
                throw new ResponseValidationException($"Invalid response format from {providerName}", ResponseValidationType.Format);

            if (!await validator.DetectErrorsAsync(response))
                throw new ResponseValidationException($"Error detected in response from {providerName}", ResponseValidationType.Error);
        }

        /// <summary>
        /// Creates an instance of the specified LLM provider
        /// </summary>
        /// <param name="providerName">Name of the provider to create</param>
        /// <returns>An ILLMProvider instance</returns>
        /// <exception cref="ArgumentException">Thrown when provider is not supported</exception>
        /// <exception cref="InvalidOperationException">Thrown when provider cannot be instantiated</exception>
        public ILLMProvider CreateProvider(string providerName = null)
        {
            // If fallback is enabled and no specific provider is requested, create a fallback provider
            if (_fallbackSettings?.Enabled == true && string.IsNullOrEmpty(providerName))
            {
                return CreateFallbackProvider();
            }

            if (string.IsNullOrEmpty(providerName))
            {
                throw new ArgumentException("Provider name cannot be empty when fallback is disabled", nameof(providerName));
            }

            ILLMProvider provider = providerName.ToLowerInvariant() switch
            {
                "anthropic" => CreateAnthropicProvider(),
                "openai" => CreateOpenAIProvider(),
                "azureopenai" => CreateAzureOpenAIProvider(),
                _ => throw new ArgumentException($"Unsupported LLM provider: {providerName}. " +
                    $"Supported providers are: Anthropic, OpenAI, AzureOpenAI")
            };

            ValidateProvider(provider);
            return provider;
        }

        private ILLMProvider CreateAnthropicProvider()
        {
            try
            {
                var client = _serviceProvider.GetRequiredService<AnthropicClient>();
                if (client == null)
                {
                    throw new InvalidOperationException("Failed to create Anthropic client");
                }

                // Decorate the client with validation
                return new ValidatingLLMProvider(client, "anthropic", this);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("Failed to initialize Anthropic provider", ex);
            }
        }

        private ILLMProvider CreateOpenAIProvider()
        {
            try
            {
                var client = _serviceProvider.GetRequiredService<OpenAIClient>();
                if (client == null)
                {
                    throw new InvalidOperationException("Failed to create OpenAI client");
                }

                // Decorate the client with validation
                return new ValidatingLLMProvider(client, "openai", this);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("Failed to initialize OpenAI provider", ex);
            }
        }

        private ILLMProvider CreateFallbackProvider()
        {
            var providers = new List<ILLMProvider>();

            foreach (var providerConfig in _fallbackSettings.Providers)
            {
                try
                {
                    var provider = CreateProvider(providerConfig.Name);
                    providers.Add(provider);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to initialize provider {providerConfig.Name} for fallback chain");
                }
            }

            if (!providers.Any())
            {
                throw new InvalidOperationException("No providers could be initialized for fallback chain");
            }

            return new FallbackLLMProvider(
                providers,
                _fallbackSettings,
                _serviceProvider.GetRequiredService<ILogger<FallbackLLMProvider>>(),
                _serviceProvider.GetRequiredService<LLMProviderMetrics>(),
                _serviceProvider.GetRequiredService<IRateLimitingService>());
        }

        private ILLMProvider CreateAzureOpenAIProvider()
        {
            try
            {
                var client = _serviceProvider.GetRequiredService<AzureOpenAIClient>();
                if (client == null)
                {
                    throw new InvalidOperationException("Failed to create Azure OpenAI client");
                }

                // Decorate the client with validation
                return new ValidatingLLMProvider(client, "azureopenai", this);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("Failed to initialize Azure OpenAI provider", ex);
            }
        }

        private void ValidateProvider(ILLMProvider provider)
        {
            if (string.IsNullOrEmpty(provider.ProviderName))
            {
                throw new InvalidOperationException("Provider name cannot be empty");
            }

            if (string.IsNullOrEmpty(provider.ModelName))
            {
                throw new InvalidOperationException("Model name cannot be empty");
            }
        }
    }
}