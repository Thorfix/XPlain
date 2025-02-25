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
}