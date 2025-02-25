using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class PreWarmingStrategy
    {
        public Dictionary<string, PreWarmPriority> KeyPriorities { get; set; } = new();
        public int BatchSize { get; set; } = 10;
        public TimeSpan PreWarmInterval { get; set; } = TimeSpan.FromMinutes(15);
        public double ResourceThreshold { get; set; } = 0.7;
        public Dictionary<string, DateTime> OptimalTimings { get; set; } = new();
    }
    
    public enum EvictionStrategy
    {
        LRU,
        HitRateWeighted,
        SizeWeighted,
        Adaptive
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
}