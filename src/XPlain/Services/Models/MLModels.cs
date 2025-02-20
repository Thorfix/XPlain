using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class MLModelTrainingData
    {
        public DateTime Timestamp { get; set; }
        public string ActionType { get; set; }
        public Dictionary<string, double> MetricsBefore { get; set; }
        public Dictionary<string, double> MetricsAfter { get; set; }
        public bool WasSuccessful { get; set; }
        public double OptimizationImpact { get; set; }
        public OptimizationContext Context { get; set; }
    }

    public class OptimizationContext
    {
        public long CacheSize { get; set; }
        public ICacheEvictionPolicy EvictionPolicy { get; set; }
        public WorkloadCharacteristics WorkloadCharacteristics { get; set; }
    }

    public class WorkloadCharacteristics
    {
        public double ReadWriteRatio { get; set; }
        public double SequentialAccessRatio { get; set; }
        public double DataSizeDistribution { get; set; }
        public double RequestFrequencyVariation { get; set; }
        public Dictionary<string, double> AccessPatterns { get; set; }
    }