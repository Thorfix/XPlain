using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class PreWarmingMetrics
    {
        public int TotalAttempts { get; set; }
        public int SuccessfulPreWarms { get; set; }
        public double SuccessRate => TotalAttempts > 0 ? (double)SuccessfulPreWarms / TotalAttempts : 0;
        public double AverageResourceUsage { get; set; }
        public double CacheHitImprovementPercent { get; set; }
        public double AverageResponseTimeImprovement { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    }
}