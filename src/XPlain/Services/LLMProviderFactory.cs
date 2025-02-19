using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    /// <summary>
    /// Factory class for creating LLM provider instances
    /// </summary>
    public class LLMProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly LLMSettings _llmSettings;

        public LLMProviderFactory(IServiceProvider serviceProvider, IOptions<LLMSettings> settings)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _llmSettings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Creates an instance of the specified LLM provider
        /// </summary>
        /// <param name="providerName">Name of the provider to create</param>
        /// <returns>An ILLMProvider instance</returns>
        /// <exception cref="ArgumentException">Thrown when provider is not supported</exception>
        /// <exception cref="InvalidOperationException">Thrown when provider cannot be instantiated</exception>
        public ILLMProvider CreateProvider(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
            {
                throw new ArgumentException("Provider name cannot be empty", nameof(providerName));
            }

            ILLMProvider provider = providerName.ToLowerInvariant() switch
            {
                "anthropic" => CreateAnthropicProvider(),
                "openai" => CreateOpenAIProvider(),
                _ => throw new ArgumentException($"Unsupported LLM provider: {providerName}. " +
                    $"Supported providers are: Anthropic, OpenAI")
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
                return client;
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
                return client;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("Failed to initialize OpenAI provider", ex);
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