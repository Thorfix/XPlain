using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class CacheInvalidationEvent
    {
        public DateTime Time { get; set; }
        public string Reason { get; set; }
        public int ItemsEvicted { get; set; }
    }

    public class CachePerformanceMetrics
    {
        public double AverageResponseTime { get; set; }
        public double PerformanceGain { get; set; }
        public int HitCount { get; set; }
        public int MissCount { get; set; }
    }

    public class CacheStats
    {
        public long StorageUsageBytes { get; set; }
        public int CachedItemCount { get; set; }
        public Dictionary<string, double> AverageResponseTimes { get; set; }
        public Dictionary<string, CachePerformanceMetrics> PerformanceByQueryType { get; set; }
        public List<CacheInvalidationEvent> InvalidationHistory { get; set; }

        public CacheStats()
        {
            AverageResponseTimes = new Dictionary<string, double>();
            PerformanceByQueryType = new Dictionary<string, CachePerformanceMetrics>();
            InvalidationHistory = new List<CacheInvalidationEvent>();
        }
    }

    public class PreWarmMetrics
    {
        public long UsageFrequency { get; set; }
        public DateTime LastAccessed { get; set; }
        public double AverageResponseTime { get; set; }
        public double PerformanceImpact { get; set; }
        public long ResourceCost { get; set; }
        public double PredictedValue { get; set; }
        public PreWarmPriority RecommendedPriority { get; set; }
    }

    public enum PreWarmPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class PreWarmingStrategy
    {
        public Dictionary<string, PreWarmPriority> KeyPriorities { get; set; }
        public int BatchSize { get; set; }
        public TimeSpan PreWarmInterval { get; set; }
        public double ResourceThreshold { get; set; }
        public Dictionary<string, DateTime> OptimalTimings { get; set; }

        public PreWarmingStrategy()
        {
            KeyPriorities = new Dictionary<string, PreWarmPriority>();
            OptimalTimings = new Dictionary<string, DateTime>();
        }
    }
}