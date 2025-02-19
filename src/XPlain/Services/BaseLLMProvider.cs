using System;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace XPlain.Services
{
    public abstract class BaseLLMProvider : ILLMProvider
    {
        protected readonly ICacheProvider _cacheProvider;

        protected BaseLLMProvider(ICacheProvider cacheProvider)
        {
            _cacheProvider = cacheProvider;
        }

        public abstract string ProviderName { get; }
        public abstract string ModelName { get; }

        protected abstract Task<string> GetCompletionInternalAsync(string prompt);

        public async Task<string> GetCompletionAsync(string prompt)
        {
            // Generate cache key from prompt
            var cacheKey = GenerateCacheKey(prompt);

            // Try to get from cache first
            var cachedResponse = await _cacheProvider.GetAsync<string>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            // Get new completion
            var response = await GetCompletionInternalAsync(prompt);

            // Cache the response
            await _cacheProvider.SetAsync(cacheKey, response);

            return response;
        }

        private string GenerateCacheKey(string prompt)
        {
            // Include model info in cache key to ensure different models get different caches
            var keyInput = $"{ProviderName}:{ModelName}:{prompt}";
            
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyInput));
            return Convert.ToBase64String(hashBytes);
        }
    }
}