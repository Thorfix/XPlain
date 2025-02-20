using Microsoft.ML.Data;

namespace XPlain.Services
{
    public class MetricPrediction
    {
        [ColumnName("Score")]
        public float PredictedValue { get; set; }

        [ColumnName("Probability")]
        public float Confidence { get; set; }
    }

    public class PerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public double CacheHitRate { get; set; }
        public double MemoryUsage { get; set; }
        public double AverageResponseTime { get; set; }
        public double SystemCpuUsage { get; set; }
        public double SystemMemoryUsage { get; set; }
        public int ActiveConnections { get; set; }

        public double GetMetricValue(string metricType)
        {
            return metricType switch
            {
                "CacheHitRate" => CacheHitRate,
                "MemoryUsage" => MemoryUsage,
                "AverageResponseTime" => AverageResponseTime,
                _ => throw new ArgumentException($"Unknown metric type: {metricType}")
            };
        }
    }

    public class MonitoringThresholds
    {
        public double MinHitRatio { get; set; }
        public double MaxMemoryUsageMB { get; set; }
        public double MaxResponseTimeMs { get; set; }
    }
}