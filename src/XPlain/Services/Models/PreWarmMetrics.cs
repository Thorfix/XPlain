using System;

namespace XPlain.Services
{
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
    
    public class PreWarmingMetrics
    {
        public long TotalAttempts { get; set; }
        public long SuccessfulPreWarms { get; set; }
        public double AverageResourceUsage { get; set; }
        public double CacheHitImprovementPercent { get; set; }
        public double AverageResponseTimeImprovement { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        
        public double SuccessRate => TotalAttempts > 0 ? (double)SuccessfulPreWarms / TotalAttempts : 0;
    }
}