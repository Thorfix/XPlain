using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace XPlain.Services
{
    public record CacheStats
    {
        public long Hits { get; init; }
        public long Misses { get; init; }
        public double HitRatio => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
        public long StorageUsageBytes { get; init; }
        public int CachedItemCount { get; init; }
        public Dictionary<string, int> QueryTypeStats { get; init; } = new();
        public List<(string Query, int Frequency)> TopQueries { get; init; } = new();
        public Dictionary<string, double> AverageResponseTimes { get; init; } = new();
        public DateTime LastStatsUpdate { get; init; }
    }

    public interface ICacheProvider
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
        Task RemoveAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task InvalidateOnCodeChangeAsync(string codeHash);
        Task WarmupCacheAsync(string[] frequentQuestions, string codeContext);
        CacheStats GetCacheStats();
        Task LogQueryStatsAsync(string queryType, string query, double responseTime, bool wasCached);
    }
}