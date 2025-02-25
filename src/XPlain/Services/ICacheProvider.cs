using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheProvider
    {
        Task<bool> IsKeyFresh(string key);
        Task<bool> PreWarmKey(string key, PreWarmPriority priority = PreWarmPriority.Medium);
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
        Task RemoveAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task WarmupCacheAsync(string[] questions, string codeContext);
        Task InvalidateOnCodeChangeAsync(string codeHash);
        Task<string> GeneratePerformanceChartAsync(OutputFormat format);
        Task<List<string>> GetCacheWarmingRecommendationsAsync();
        Task LogQueryStatsAsync(string queryType, string query, double responseTime, bool hit);
        CacheStats GetCacheStats();
        Task AddEventListener(ICacheEventListener listener);
        Task RemoveEventListener(ICacheEventListener listener);
    }

    public enum PreWarmPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class CacheStats
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public double HitRatio => (Hits + Misses) == 0 ? 0 : (double)Hits / (Hits + Misses);
        public long CachedItemCount { get; set; }
        public long StorageUsageBytes { get; set; }
        public Dictionary<string, long> QueryTypeStats { get; set; } = new();
        public Dictionary<string, double> AverageResponseTimes { get; set; } = new();
        public Dictionary<string, CachePerformanceMetrics> PerformanceByQueryType { get; set; } = new();
        public List<CacheInvalidationEvent> InvalidationHistory { get; set; } = new();
        public long InvalidationCount { get; set; }
        public Dictionary<string, int> TopQueries { get; set; } = new();
        public DateTime LastStatsUpdate { get; set; } = DateTime.UtcNow;
    }

    public class CachePerformanceMetrics
    {
        public double CachedResponseTime { get; set; }
        public double NonCachedResponseTime { get; set; }
        public double PerformanceGain => NonCachedResponseTime > 0 
            ? ((NonCachedResponseTime - CachedResponseTime) / NonCachedResponseTime) * 100 
            : 0;
    }

    public class CacheInvalidationEvent
    {
        public string Reason { get; set; }
        public DateTime Time { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }