using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class PredictionResult
    {
        public double Value { get; set; }
        public double Confidence { get; set; }
        public TimeSpan TimeToImpact { get; set; }
    }

    public class TrendAnalysis
    {
        public string Trend { get; set; }
        public double CurrentValue { get; set; }
        public double ProjectedValue { get; set; }
        public DateTime ProjectionTime { get; set; }
        public double ChangePercent { get; set; }
    }

    public class PrecursorPattern
    {
        public string TargetIssue { get; set; }
        public double Confidence { get; set; }
        public TimeSpan LeadTime { get; set; }
        public List<MetricSequence> Sequences { get; set; } = new();
    }

    public class MetricSequence
    {
        public string MetricName { get; set; }
        public List<double> Values { get; set; } = new();
        public double Correlation { get; set; }
    }
}