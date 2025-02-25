using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class PrecursorPattern
    {
        public string TargetIssue { get; set; }
        public double Confidence { get; set; }
        public TimeSpan LeadTime { get; set; }
        public List<MetricSequence> Sequences { get; set; } = new List<MetricSequence>();
    }

    public class MetricSequence
    {
        public string MetricName { get; set; }
        public double Correlation { get; set; }
        public List<double> Values { get; set; } = new List<double>();
    }
}