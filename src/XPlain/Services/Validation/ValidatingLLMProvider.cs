using System;
using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Decorator that adds validation to any LLM provider
    /// </summary>
    public class ValidatingLLMProvider : ILLMProvider
    {
        private readonly ILLMProvider _innerProvider;
        private readonly string _providerName;
        private readonly LLMProviderFactory _factory;

        public ValidatingLLMProvider(ILLMProvider provider, string providerName, LLMProviderFactory factory)
        {
            _innerProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public string ProviderName => _innerProvider.ProviderName;

        public string ModelName => _innerProvider.ModelName;

        public async Task<string> GenerateResponseAsync(string prompt)
        {
            var response = await _innerProvider.GenerateResponseAsync(prompt);
            await _factory.ValidateResponseAsync(_providerName, response);
            return response;
        }

        public async Task<string> GenerateResponseFromJsonAsync(string jsonPrompt)
        {
            var response = await _innerProvider.GenerateResponseFromJsonAsync(jsonPrompt);
            await _factory.ValidateResponseAsync(_providerName, response);
            return response;
        }

        public void Dispose()
        {
            _innerProvider.Dispose();
        }
    }
}