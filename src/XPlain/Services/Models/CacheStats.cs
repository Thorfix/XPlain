using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class CacheStats
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long CachedItemCount { get; set; }
        public long StorageUsageBytes { get; set; }
        public Dictionary<string, long> QueryTypeStats { get; set; } = new Dictionary<string, long>();
        public Dictionary<string, double> AverageResponseTimes { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, CachePerformanceMetrics> PerformanceByQueryType { get; set; } = new Dictionary<string, CachePerformanceMetrics>();
        public List<CacheInvalidationEvent> InvalidationHistory { get; set; } = new List<CacheInvalidationEvent>();
        public long InvalidationCount { get; set; }
        public Dictionary<string, int> TopQueries { get; set; } = new Dictionary<string, int>();
        public DateTime LastStatsUpdate { get; set; } = DateTime.UtcNow;
        
        public double HitRatio => (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) : 0;
    }

    public class CachePerformanceMetrics
    {
        public double PerformanceGain { get; set; }
        public double CachedResponseTime { get; set; }
        public double NonCachedResponseTime { get; set; }
    }

    public class CacheInvalidationEvent
    {
        public string Reason { get; set; }
        public DateTime Time { get; set; } = DateTime.UtcNow;
    }
}