using System;
using System.Collections.Generic;
using System.Text.Json;

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

    public class ModelVersion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ModelName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, double> Metrics { get; set; } = new();
        public Dictionary<string, object> TrainingParameters { get; set; } = new();
        public ModelMetadata Metadata { get; set; } = new();
        public bool IsActive { get; set; }
        public string Status { get; set; } = "created";
    }

    public class ModelMetadata
    {
        public int DatasetSize { get; set; }
        public DateTime TrainingStartTime { get; set; }
        public DateTime TrainingEndTime { get; set; }
        public Dictionary<string, string> DatasetCharacteristics { get; set; } = new();
        public List<string> Features { get; set; } = new();
        public Dictionary<string, double> ValidationMetrics { get; set; } = new();
        public string Description { get; set; } = "";
        
        public override string ToString()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public class ModelComparisonResult
    {
        public string BaselineModelId { get; set; } = "";
        public string CandidateModelId { get; set; } = "";
        public Dictionary<string, double> MetricDifferences { get; set; } = new();
        public Dictionary<string, string> Observations { get; set; } = new();
        public bool IsCandidateBetter { get; set; }
        public string RecommendedAction { get; set; } = "";
        public ValidationResults CrossValidation { get; set; } = new();
    }

    public class ValidationResults
    {
        public double AverageAccuracy { get; set; }
        public double StandardDeviation { get; set; }
        public List<FoldResult> FoldResults { get; set; } = new();
    }

    public class FoldResult
    {
        public int FoldNumber { get; set; }
        public Dictionary<string, double> Metrics { get; set; } = new();
        public Dictionary<string, double> FeatureImportance { get; set; } = new();
    }

    public class ModelHealthReport
    {
        public string ModelId { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsHealthy { get; set; }
        public Dictionary<string, double> Metrics { get; set; } = new();
        public string ErrorMessage { get; set; } = "";
        public DateTime LastUpdated { get; set; }
    }

    public class PredictionMetrics
    {
        public double Accuracy { get; set; }
        public double AverageLatency { get; set; }
        public int TotalPredictions { get; set; }
        public Dictionary<string, int> ErrorCounts { get; set; } = new();
    }

    public class DataDriftReport
    {
        public bool SignificantDriftDetected { get; set; }
        public double DriftScore { get; set; }
        public Dictionary<string, FeatureDrift> FeatureDrifts { get; set; } = new();
        public DateTime AnalysisTime { get; set; }
    }

    public class FeatureDrift
    {
        public string FeatureName { get; set; } = "";
        public double DriftMagnitude { get; set; }
        public string DriftType { get; set; } = "";
        public Dictionary<string, double> Statistics { get; set; } = new();
    }