using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public interface ICacheEvictionPolicy
    {
        /// <summary>
        /// Selects items for eviction based on the policy's strategy
        /// </summary>
        /// <param name="items">List of cache items with their stats</param>
        /// <param name="targetSize">Target size in bytes to achieve after eviction</param>
        /// <returns>List of item keys to evict</returns>
        IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize);

        /// <summary>
        /// Updates the policy's internal statistics based on cache access patterns
        /// </summary>
        /// <param name="stats">Current cache access statistics</param>
        void UpdatePolicy(CacheAccessStats stats);

        /// <summary>
        /// Gets the current policy metrics for monitoring
        /// </summary>
        /// <returns>Dictionary of metric name/value pairs</returns>
        Dictionary<string, double> GetPolicyMetrics();
    }

    public record CacheItemStats
    {
        public long Size { get; init; }
        public DateTime LastAccess { get; init; }
        public DateTime CreationTime { get; init; }
        public int AccessCount { get; init; }
        public long EvictionCount { get; init; }
        public double AverageResponseTime { get; init; }
        public string QueryComplexity { get; init; }
    }

    public record CacheAccessStats
    {
        public long TotalHits { get; init; }
        public long TotalMisses { get; init; }
        public Dictionary<string, int> QueryTypeFrequency { get; init; } = new();
        public Dictionary<string, double> AverageResponseTimes { get; init; } = new();
        public double ReadWriteRatio { get; init; }
        public Dictionary<string, string> AccessPatterns { get; init; } = new();
        public Dictionary<string, double> DataSizeDistribution { get; init; } = new();
        public Dictionary<string, List<DateTime>> TemporalPatterns { get; init; } = new();
        public Dictionary<string, double> PolicyPerformance { get; init; } = new();
        public double MemoryUtilization { get; init; }
        public double HitRateImpact { get; init; }
        public int UnnecessaryEvictions { get; init; }
    }

    public interface IEvictionPolicyMetrics
    {
        double HitRate { get; }
        double MemoryEfficiency { get; }
        double AverageResponseTime { get; }
        double ResourceUtilization { get; }
        double EvictionAccuracy { get; }
        Dictionary<string, double> CustomMetrics { get; }
    }

    public record PolicySwitchEvent
    {
        public string FromPolicy { get; init; }
        public string ToPolicy { get; init; }
        public DateTime Timestamp { get; init; }
        public Dictionary<string, double> PerformanceImpact { get; init; }
        public string Reason { get; init; }
    }
}