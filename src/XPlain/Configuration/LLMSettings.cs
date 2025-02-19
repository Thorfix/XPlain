using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace XPlain.Configuration
{
    /// <summary>
    /// Base settings class for LLM providers
    /// </summary>
    public class LLMSettings
    {
        private static readonly string[] SupportedProviders = { "Anthropic" };

        /// <summary>
        /// The type of LLM provider to use
        /// </summary>
        [Required(ErrorMessage = "LLM provider type is required")]
        public string Provider { get; set; } = "Anthropic";

        /// <summary>
        /// The model to use for the selected provider
        /// </summary>
        [Required(ErrorMessage = "Model name is required")]
        public string Model { get; set; } = "claude-3-5-sonnet-latest";

        /// <summary>
        /// The API key for the LLM service
        /// </summary>
        [Required(ErrorMessage = "API key is required")]
        public string ApiKey { get; set; }

        /// <summary>
        /// Validates that the provider is supported
        /// </summary>
        public virtual void Validate()
        {
            if (string.IsNullOrEmpty(Provider))
            {
                throw new ValidationException("Provider cannot be empty");
            }

            if (string.IsNullOrEmpty(Model))
            {
                throw new ValidationException("Model cannot be empty");
            }

            if (string.IsNullOrEmpty(ApiKey))
            {
                throw new ValidationException("API key cannot be empty");
            }

            if (!SupportedProviders.Contains(Provider, StringComparer.OrdinalIgnoreCase))
            {
                throw new ValidationException($"Unsupported provider: {Provider}. Supported providers are: {string.Join(", ", supportedProviders)}");
            }
        }
    }
}