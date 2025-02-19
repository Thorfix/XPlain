using System;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheProvider
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
        Task RemoveAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task InvalidateOnCodeChangeAsync(string codeHash);
        Task WarmupCacheAsync(string[] frequentQuestions, string codeContext);
        (long hits, long misses) GetCacheStats();
    }
}