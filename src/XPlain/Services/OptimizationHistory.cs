using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class OptimizationAction
    {
        public string ActionType { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, double> MetricsBefore { get; set; }
        public Dictionary<string, double> MetricsAfter { get; set; }
        public bool WasSuccessful { get; set; }
        public TimeSpan EffectDuration { get; set; }
        public string RollbackReason { get; set; }
    }

    public class OptimizationStrategy
    {
        public string Trigger { get; set; }
        public string Action { get; set; }
        public double SuccessRate { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public List<OptimizationAction> History { get; set; } = new();
    }
}