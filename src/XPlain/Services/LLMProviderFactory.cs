using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class LLMProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public LLMProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ILLMProvider CreateProvider(string providerName)
        {
            return providerName.ToLowerInvariant() switch
            {
                "anthropic" => _serviceProvider.GetRequiredService<AnthropicClient>(),
                _ => throw new ArgumentException($"Unsupported LLM provider: {providerName}")
            };
        }
    }
}