using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class PreWarmMetrics
    {
        /// <summary>
        /// Frequency of usage for this query
        /// </summary>
        public long UsageFrequency { get; set; }
        
        /// <summary>
        /// When this query was last accessed
        /// </summary>
        public DateTime LastAccessed { get; set; }
        
        /// <summary>
        /// Average response time for this query
        /// </summary>
        public double AverageResponseTime { get; set; }
        
        /// <summary>
        /// Performance improvement when cached vs non-cached (percentage)
        /// </summary>
        public double PerformanceImpact { get; set; }
        
        /// <summary>
        /// Size of the cached value in bytes
        /// </summary>
        public long ResourceCost { get; set; }
        
        /// <summary>
        /// ML-predicted value of caching this item (0-1 scale)
        /// </summary>
        public double PredictedValue { get; set; }
        
        /// <summary>
        /// Recommended priority for pre-warming
        /// </summary>
        public PreWarmPriority RecommendedPriority { get; set; }
    }

    public class PreWarmingStrategy
    {
        /// <summary>
        /// Mapping of keys to their pre-warming priority
        /// </summary>
        public Dictionary<string, PreWarmPriority> KeyPriorities { get; set; } = new();
        
        /// <summary>
        /// Optimal batch size for pre-warming operations
        /// </summary>
        public int BatchSize { get; set; }
        
        /// <summary>
        /// Optimal time interval between pre-warming cycles
        /// </summary>
        public TimeSpan PreWarmInterval { get; set; }
        
        /// <summary>
        /// Resource threshold beyond which pre-warming should pause
        /// </summary>
        public double ResourceThreshold { get; set; }
        
        /// <summary>
        /// Optimal timing predictions for specific queries
        /// </summary>
        public Dictionary<string, DateTime> OptimalTimings { get; set; } = new();
    }
}