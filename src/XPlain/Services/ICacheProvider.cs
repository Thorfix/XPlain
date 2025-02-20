using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace XPlain.Services
{
    public enum PreWarmPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public record PreWarmMetrics
    {
        public double UsageFrequency { get; init; }
        public double AverageResponseTime { get; init; }
        public double PerformanceImpact { get; init; }
        public DateTime LastAccessed { get; init; }
        public PreWarmPriority RecommendedPriority { get; init; }
        public double ResourceCost { get; init; }
        public double PredictedValue { get; init; }
    }

    public record PreWarmingStrategy
    {
        public Dictionary<string, PreWarmPriority> KeyPriorities { get; init; } = new();
        public TimeSpan PreWarmInterval { get; init; }
        public int BatchSize { get; init; }
        public double ResourceThreshold { get; init; }
        public Dictionary<string, DateTime> OptimalTimings { get; init; } = new();
    }

    public record CachePerformanceMetrics
    {
        public double CachedResponseTime { get; init; }
        public double NonCachedResponseTime { get; init; }
        public double PerformanceGain => NonCachedResponseTime == 0 ? 0 : (NonCachedResponseTime - CachedResponseTime) / NonCachedResponseTime * 100;
    }

    public record PreWarmingMetrics
    {
        public int TotalAttempts { get; init; }
        public int SuccessfulPreWarms { get; init; }
        public double SuccessRate => TotalAttempts == 0 ? 0 : (double)SuccessfulPreWarms / TotalAttempts;
        public double AverageResourceUsage { get; init; }
        public double CacheHitImprovementPercent { get; init; }
        public double AverageResponseTimeImprovement { get; init; }
        public DateTime LastUpdate { get; init; }
    }

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
        public Dictionary<string, CachePerformanceMetrics> PerformanceByQueryType { get; init; } = new();
        public int InvalidationCount { get; init; }
        public List<(DateTime Time, int ItemsInvalidated)> InvalidationHistory { get; init; } = new();
        public DateTime LastStatsUpdate { get; init; }
    }

    public record CacheAnalytics
    {
        public DateTime Timestamp { get; init; }
        public CacheStats Stats { get; init; } = default!;
        public double MemoryUsageMB { get; init; }
        public List<string> RecommendedOptimizations { get; init; } = new();
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
        Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(DateTime since);
        Task LogAnalyticsAsync();
        Task<List<string>> GetCacheWarmingRecommendationsAsync();
        Task<string> GeneratePerformanceChartAsync(OutputFormat format);
        
        // New adaptive cache warming methods
        Task<Dictionary<string, PreWarmMetrics>> GetPreWarmCandidatesAsync();
        Task<bool> PreWarmBatchAsync(IEnumerable<string> keys, PreWarmPriority priority);
        Task<PreWarmingStrategy> OptimizePreWarmingStrategyAsync();
    }
}