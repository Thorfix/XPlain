using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting
{
    public class LoadTestReportGenerator
    {
        private readonly LoadTestEngine _loadTestEngine;
        private readonly MLPredictionService _mlPredictionService;
        private readonly ICacheProvider _cacheProvider;

        public LoadTestReportGenerator(
            LoadTestEngine loadTestEngine,
            MLPredictionService mlPredictionService,
            ICacheProvider cacheProvider)
        {
            _loadTestEngine = loadTestEngine;
            _mlPredictionService = mlPredictionService;
            _cacheProvider = cacheProvider;
        }

        public async Task<string> GenerateReport(LoadTestMetrics metrics, string format = "markdown")
        {
            var report = new StringBuilder();
            var predictedVsActual = await AnalyzePredictionAccuracy();
            var performanceBoundaries = await AnalyzePerformanceBoundaries(metrics);
            var trainingData = await CollectTrainingData(metrics);

            switch (format.ToLower())
            {
                case "markdown":
                    return await GenerateMarkdownReport(metrics, predictedVsActual, performanceBoundaries, trainingData);
                case "json":
                    return await GenerateJsonReport(metrics, predictedVsActual, performanceBoundaries, trainingData);
                default:
                    return await GenerateTextReport(metrics, predictedVsActual, performanceBoundaries, trainingData);
            }
        }

        private async Task<string> GenerateMarkdownReport(
            LoadTestMetrics metrics,
            PredictionAnalysis predictedVsActual,
            PerformanceAnalysis boundaries,
            TrainingDataSummary trainingData)
        {
            var report = new StringBuilder();
            
            report.AppendLine("# Load Test Performance Report");
            report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            report.AppendLine();

            report.AppendLine("## Test Configuration");
            report.AppendLine($"- Active Users: {metrics.ActiveUsers}");
            report.AppendLine($"- Total Requests: {metrics.TotalRequests}");
            report.AppendLine($"- Test Duration: {metrics.TestDuration.TotalMinutes:F1} minutes");
            report.AppendLine();

            report.AppendLine("## Overall Performance");
            report.AppendLine($"- Average Response Time: {metrics.AverageResponseTime:F2}ms");
            report.AppendLine($"- Error Rate: {metrics.ErrorRate:P2}");
            report.AppendLine($"- Cache Hit Rate: {metrics.CacheHitRate:P2}");
            report.AppendLine();

            report.AppendLine("## ML Prediction Performance");
            report.AppendLine("| Metric | Value |");
            report.AppendLine("|--------|-------|");
            report.AppendLine($"| Prediction Accuracy | {predictedVsActual.OverallAccuracy:P2} |");
            report.AppendLine($"| False Positives | {predictedVsActual.FalsePositives} |");
            report.AppendLine($"| False Negatives | {predictedVsActual.FalseNegatives} |");
            report.AppendLine($"| True Positives | {predictedVsActual.TruePositives} |");
            report.AppendLine($"| True Negatives | {predictedVsActual.TrueNegatives} |");
            report.AppendLine();

            report.AppendLine("## System Boundaries");
            report.AppendLine("| Metric | Threshold |");
            report.AppendLine("|--------|-----------|");
            report.AppendLine($"| Max Concurrent Users | {boundaries.MaxConcurrentUsers} |");
            report.AppendLine($"| Response Time Threshold | {boundaries.ResponseTimeThreshold}ms |");
            report.AppendLine($"| Memory Usage Limit | {boundaries.MemoryUsageLimit}MB |");
            report.AppendLine($"| Cache Size Limit | {boundaries.CacheSizeLimit} items |");
            report.AppendLine();

            report.AppendLine("## Training Data Summary");
            report.AppendLine($"- Total Samples Collected: {trainingData.TotalSamples}");
            report.AppendLine($"- Unique Query Patterns: {trainingData.UniquePatterns}");
            report.AppendLine($"- Data Quality Score: {trainingData.QualityScore:P2}");
            report.AppendLine();

            report.AppendLine("## Recommendations");
            foreach (var recommendation in boundaries.Recommendations)
            {
                report.AppendLine($"- {recommendation}");
            }

            return report.ToString();
        }

        private async Task<string> GenerateJsonReport(
            LoadTestMetrics metrics,
            PredictionAnalysis predictedVsActual,
            PerformanceAnalysis boundaries,
            TrainingDataSummary trainingData)
        {
            var report = new
            {
                timestamp = DateTime.UtcNow,
                configuration = new
                {
                    activeUsers = metrics.ActiveUsers,
                    totalRequests = metrics.TotalRequests,
                    testDuration = metrics.TestDuration.TotalMinutes
                },
                performance = new
                {
                    averageResponseTime = metrics.AverageResponseTime,
                    errorRate = metrics.ErrorRate,
                    cacheHitRate = metrics.CacheHitRate
                },
                mlPredictions = new
                {
                    accuracy = predictedVsActual.OverallAccuracy,
                    falsePositives = predictedVsActual.FalsePositives,
                    falseNegatives = predictedVsActual.FalseNegatives,
                    truePositives = predictedVsActual.TruePositives,
                    trueNegatives = predictedVsActual.TrueNegatives
                },
                boundaries = new
                {
                    maxUsers = boundaries.MaxConcurrentUsers,
                    responseTimeThreshold = boundaries.ResponseTimeThreshold,
                    memoryLimit = boundaries.MemoryUsageLimit,
                    cacheLimit = boundaries.CacheSizeLimit,
                    recommendations = boundaries.Recommendations
                },
                trainingData = new
                {
                    totalSamples = trainingData.TotalSamples,
                    uniquePatterns = trainingData.UniquePatterns,
                    qualityScore = trainingData.QualityScore
                }
            };

            return System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private async Task<string> GenerateTextReport(
            LoadTestMetrics metrics,
            PredictionAnalysis predictedVsActual,
            PerformanceAnalysis boundaries,
            TrainingDataSummary trainingData)
        {
            var report = new StringBuilder();
            
            report.AppendLine("Load Test Performance Report");
            report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            report.AppendLine(new string('-', 50));
            
            // Similar to markdown report but without formatting
            // ... Format text version ...

            return report.ToString();
        }

        private async Task<PredictionAnalysis> AnalyzePredictionAccuracy()
        {
            var metrics = await _loadTestEngine.GetCurrentMetricsAsync();
            var analysis = new PredictionAnalysis();

            foreach (var metric in metrics.CustomMetrics)
            {
                if (metric.Key == "prediction_accuracy")
                {
                    analysis.OverallAccuracy = metric.Value;
                }
                // ... analyze other prediction metrics ...
            }

            return analysis;
        }

        private async Task<PerformanceAnalysis> AnalyzePerformanceBoundaries(LoadTestMetrics metrics)
        {
            return new PerformanceAnalysis
            {
                MaxConcurrentUsers = metrics.ActiveUsers,
                ResponseTimeThreshold = metrics.AverageResponseTime * 2,
                MemoryUsageLimit = metrics.CustomMetrics.GetValueOrDefault("memory_usage_mb", 0),
                CacheSizeLimit = (int)metrics.CustomMetrics.GetValueOrDefault("cache_size", 0),
                Recommendations = GenerateRecommendations(metrics)
            };
        }

        private async Task<TrainingDataSummary> CollectTrainingData(LoadTestMetrics metrics)
        {
            return new TrainingDataSummary
            {
                TotalSamples = metrics.TotalRequests,
                UniquePatterns = metrics.CustomMetrics.Count,
                QualityScore = CalculateDataQuality(metrics)
            };
        }

        private List<string> GenerateRecommendations(LoadTestMetrics metrics)
        {
            var recommendations = new List<string>();

            if (metrics.CacheHitRate < 0.7)
            {
                recommendations.Add("Consider increasing cache size or adjusting cache eviction policy");
            }

            if (metrics.AverageResponseTime > 1000)
            {
                recommendations.Add("Response times are high - investigate potential bottlenecks");
            }

            if (metrics.ErrorRate > 0.05)
            {
                recommendations.Add("Error rate exceeds 5% - review error handling and mitigation strategies");
            }

            return recommendations;
        }

        private double CalculateDataQuality(LoadTestMetrics metrics)
        {
            // Quality score based on data distribution and completeness
            var distribution = metrics.CustomMetrics.GetValueOrDefault("query_distribution_score", 0.0);
            var coverage = metrics.CustomMetrics.GetValueOrDefault("pattern_coverage_score", 0.0);
            return (distribution + coverage) / 2;
        }
    }

    public class PredictionAnalysis
    {
        public double OverallAccuracy { get; set; }
        public int TruePositives { get; set; }
        public int TrueNegatives { get; set; }
        public int FalsePositives { get; set; }
        public int FalseNegatives { get; set; }
    }

    public class PerformanceAnalysis
    {
        public int MaxConcurrentUsers { get; set; }
        public double ResponseTimeThreshold { get; set; }
        public double MemoryUsageLimit { get; set; }
        public int CacheSizeLimit { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public class TrainingDataSummary
    {
        public int TotalSamples { get; set; }
        public int UniquePatterns { get; set; }
        public double QualityScore { get; set; }
    }
}