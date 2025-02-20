using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XPlain.Services.Models;

namespace XPlain.Services
{
    public interface IModelPerformanceMonitor
    {
        Task EvaluateModelPerformance();
        Task<PerformanceMetrics> GetCurrentMetrics();
        Task<List<PerformanceMetrics>> GetHistoricalMetrics(DateTime startDate, DateTime endDate);
        Task<bool> IsPerformanceDegraded();
        Task TriggerModelRollback();
        Task<FeatureDistributionMetrics> AnalyzeFeatureDistribution();
        Task<ResourceUtilizationMetrics> GetResourceUtilization();
        Task<PerformanceStats> GetRollingWindowStats(TimeSpan window);
        Task TriggerAutomaticCorrection(PerformanceIssueType issueType);
    }

    public class ModelPerformanceMonitor : IModelPerformanceMonitor
    {
        private readonly ILogger<ModelPerformanceMonitor> _logger;
        private readonly MLModelValidationService _validationService;
        private readonly CacheMonitoringHub _monitoringHub;
        private readonly IAutomaticMitigationService _mitigationService;
        private readonly IResourceMonitor _resourceMonitor;
        private readonly MLPredictionService _predictionService;
        private readonly TimeSeriesMetricsStore _metricsStore;
        private readonly Dictionary<string, double> _performanceThresholds;
        private readonly List<PerformanceMetrics> _metricsHistory;
        private readonly Queue<PerformanceMetrics> _rollingWindow;
        private readonly int _rollingWindowSize = 100;
        private DateTime _lastFeatureAnalysis = DateTime.MinValue;
        private readonly TimeSpan _featureAnalysisInterval = TimeSpan.FromHours(1);

        public ModelPerformanceMonitor(
            ILogger<ModelPerformanceMonitor> logger,
            MLModelValidationService validationService,
            CacheMonitoringHub monitoringHub,
            IAutomaticMitigationService mitigationService,
            IResourceMonitor resourceMonitor,
            MLPredictionService predictionService,
            TimeSeriesMetricsStore metricsStore)
        {
            _logger = logger;
            _validationService = validationService;
            _monitoringHub = monitoringHub;
            _mitigationService = mitigationService;
            _resourceMonitor = resourceMonitor;
            _predictionService = predictionService;
            _metricsStore = metricsStore;
            _metricsHistory = new List<PerformanceMetrics>();
            _rollingWindow = new Queue<PerformanceMetrics>(_rollingWindowSize);
            
            // Default performance thresholds
            _performanceThresholds = new Dictionary<string, double>
            {
                { "accuracy", 0.95 },
                { "f1_score", 0.90 },
                { "precision", 0.92 },
                { "recall", 0.92 }
            };
        }

        public async Task EvaluateModelPerformance()
        {
            try
            {
                var metrics = await GetCurrentMetrics();
                _metricsHistory.Add(metrics);
                
                // Maintain rolling window
                if (_rollingWindow.Count >= _rollingWindowSize)
                {
                    _rollingWindow.Dequeue();
                }
                _rollingWindow.Enqueue(metrics);

                // Store metrics for long-term analysis
                await _metricsStore.StoreMetrics("model_performance", metrics);

                // Check for feature distribution drift
                if (DateTime.UtcNow - _lastFeatureAnalysis >= _featureAnalysisInterval)
                {
                    var distributionMetrics = await AnalyzeFeatureDistribution();
                    if (distributionMetrics.HasSignificantDrift)
                    {
                        await SendAlert(AlertSeverity.Warning, "Feature Distribution Drift Detected",
                            $"Significant drift detected in features: {string.Join(", ", distributionMetrics.DriftedFeatures)}");
                        await TriggerAutomaticCorrection(PerformanceIssueType.FeatureDrift);
                    }
                    _lastFeatureAnalysis = DateTime.UtcNow;
                }

                // Monitor resource utilization
                var resourceMetrics = await GetResourceUtilization();
                if (resourceMetrics.IsOverloaded)
                {
                    await SendAlert(AlertSeverity.Warning, "High Resource Utilization",
                        $"CPU: {resourceMetrics.CpuUsage}%, Memory: {resourceMetrics.MemoryUsage}%");
                    await TriggerAutomaticCorrection(PerformanceIssueType.ResourceOverload);
                }

                if (await IsPerformanceDegraded())
                {
                    _logger.LogWarning("Model performance degradation detected");
                    await SendAlert(AlertSeverity.Warning, "Model Performance Degradation Detected", 
                        $"Current accuracy: {metrics.Accuracy}, Below threshold: {_performanceThresholds["accuracy"]}");
                    
                    if (metrics.Accuracy < _performanceThresholds["accuracy"] * 0.9) // Severe degradation
                    {
                        await TriggerModelRollback();
                    }
                }

                await _monitoringHub.SendAsync("UpdateModelMetrics", metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during model performance evaluation");
                await SendAlert(AlertSeverity.Error, "Model Monitoring Error", ex.Message);
            }
        }

        public async Task<PerformanceMetrics> GetCurrentMetrics()
        {
            var validationResult = await _validationService.ValidateCurrentModel();
            return new PerformanceMetrics
            {
                Timestamp = DateTime.UtcNow,
                ModelVersion = validationResult.ModelVersion,
                Accuracy = validationResult.Accuracy,
                F1Score = validationResult.F1Score,
                Precision = validationResult.Precision,
                Recall = validationResult.Recall,
                LatencyMs = validationResult.LatencyMs
            };
        }

        public async Task<List<PerformanceMetrics>> GetHistoricalMetrics(DateTime startDate, DateTime endDate)
        {
            return _metricsHistory.FindAll(m => m.Timestamp >= startDate && m.Timestamp <= endDate);
        }

        public async Task<bool> IsPerformanceDegraded()
        {
            var currentMetrics = await GetCurrentMetrics();
            var stats = await GetRollingWindowStats(TimeSpan.FromHours(24));

            return currentMetrics.Accuracy < _performanceThresholds["accuracy"] ||
                   currentMetrics.F1Score < _performanceThresholds["f1_score"] ||
                   currentMetrics.Precision < _performanceThresholds["precision"] ||
                   currentMetrics.Recall < _performanceThresholds["recall"] ||
                   stats.IsStatisticallySignificantDrop();
        }

        public async Task<FeatureDistributionMetrics> AnalyzeFeatureDistribution()
        {
            var currentDistribution = await _predictionService.GetFeatureDistribution();
            var baselineDistribution = await _validationService.GetBaselineDistribution();
            
            var driftedFeatures = new List<string>();
            foreach (var feature in currentDistribution.Keys)
            {
                if (IsDistributionDriftSignificant(currentDistribution[feature], baselineDistribution[feature]))
                {
                    driftedFeatures.Add(feature);
                }
            }

            return new FeatureDistributionMetrics
            {
                Timestamp = DateTime.UtcNow,
                HasSignificantDrift = driftedFeatures.Count > 0,
                DriftedFeatures = driftedFeatures,
                CurrentDistribution = currentDistribution,
                BaselineDistribution = baselineDistribution
            };
        }

        public async Task<ResourceUtilizationMetrics> GetResourceUtilization()
        {
            var metrics = await _resourceMonitor.GetCurrentMetrics();
            return new ResourceUtilizationMetrics
            {
                Timestamp = DateTime.UtcNow,
                CpuUsage = metrics.CpuUsage,
                MemoryUsage = metrics.MemoryUsage,
                GpuUsage = metrics.GpuUsage,
                NetworkLatency = metrics.NetworkLatency,
                IsOverloaded = metrics.CpuUsage > 80 || metrics.MemoryUsage > 85
            };
        }

        public async Task<PerformanceStats> GetRollingWindowStats(TimeSpan window)
        {
            var relevantMetrics = _rollingWindow.Where(m => DateTime.UtcNow - m.Timestamp <= window).ToList();
            
            return new PerformanceStats
            {
                MeanAccuracy = relevantMetrics.Average(m => m.Accuracy),
                StdDevAccuracy = CalculateStdDev(relevantMetrics.Select(m => m.Accuracy)),
                TrendSlope = CalculateTrendSlope(relevantMetrics),
                SampleSize = relevantMetrics.Count
            };
        }

        public async Task TriggerAutomaticCorrection(PerformanceIssueType issueType)
        {
            switch (issueType)
            {
                case PerformanceIssueType.FeatureDrift:
                    await _mitigationService.TriggerModelRetraining();
                    break;
                case PerformanceIssueType.ResourceOverload:
                    await _mitigationService.ActivateSimplifiedModel();
                    break;
                case PerformanceIssueType.PerformanceDegradation:
                    await TriggerModelRollback();
                    break;
            }
        }

        private double CalculateStdDev(IEnumerable<double> values)
        {
            var avg = values.Average();
            var sumOfSquaresOfDifferences = values.Select(val => (val - avg) * (val - avg)).Sum();
            return Math.Sqrt(sumOfSquaresOfDifferences / (values.Count() - 1));
        }

        private double CalculateTrendSlope(List<PerformanceMetrics> metrics)
        {
            if (metrics.Count < 2) return 0;
            
            var xMean = metrics.Select((m, i) => (double)i).Average();
            var yMean = metrics.Select(m => m.Accuracy).Average();
            
            var numerator = metrics.Select((m, i) => ((double)i - xMean) * (m.Accuracy - yMean)).Sum();
            var denominator = metrics.Select((m, i) => Math.Pow((double)i - xMean, 2)).Sum();
            
            return denominator == 0 ? 0 : numerator / denominator;
        }

        private bool IsDistributionDriftSignificant(Dictionary<string, double> current, Dictionary<string, double> baseline)
        {
            // Implement Kolmogorov-Smirnov test or similar statistical test
            const double THRESHOLD = 0.05;
            var ksStatistic = CalculateKSStatistic(current, baseline);
            return ksStatistic > THRESHOLD;
        }

        private double CalculateKSStatistic(Dictionary<string, double> dist1, Dictionary<string, double> dist2)
        {
            // Simplified K-S statistic calculation
            var keys = dist1.Keys.Union(dist2.Keys);
            return keys.Max(k => Math.Abs(
                dist1.GetValueOrDefault(k, 0) - dist2.GetValueOrDefault(k, 0)));
        }

        public async Task TriggerModelRollback()
        {
            _logger.LogWarning("Initiating model rollback due to severe performance degradation");
            await SendAlert(AlertSeverity.Critical, "Model Rollback Initiated", 
                "Severe performance degradation detected. Rolling back to previous model version.");
            
            await _mitigationService.InitiateModelRollback();
        }

        private async Task SendAlert(AlertSeverity severity, string title, string message)
        {
            var alert = new ModelAlert
            {
                Severity = severity,
                Title = title,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            await _monitoringHub.SendAsync("ModelAlert", alert);
        }
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class PerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public string ModelVersion { get; set; }
        public double Accuracy { get; set; }
        public double F1Score { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double LatencyMs { get; set; }
    }

    public class ModelAlert
    {
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class FeatureDistributionMetrics
    {
        public DateTime Timestamp { get; set; }
        public bool HasSignificantDrift { get; set; }
        public List<string> DriftedFeatures { get; set; }
        public Dictionary<string, Dictionary<string, double>> CurrentDistribution { get; set; }
        public Dictionary<string, Dictionary<string, double>> BaselineDistribution { get; set; }
    }

    public class ResourceUtilizationMetrics
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double GpuUsage { get; set; }
        public double NetworkLatency { get; set; }
        public bool IsOverloaded { get; set; }
    }

    public class PerformanceStats
    {
        public double MeanAccuracy { get; set; }
        public double StdDevAccuracy { get; set; }
        public double TrendSlope { get; set; }
        public int SampleSize { get; set; }

        public bool IsStatisticallySignificantDrop()
        {
            const double ZSCORE_THRESHOLD = -1.96; // 95% confidence level
            return TrendSlope < 0 && (TrendSlope / (StdDevAccuracy / Math.Sqrt(SampleSize))) < ZSCORE_THRESHOLD;
        }
    }

    public enum PerformanceIssueType
    {
        FeatureDrift,
        ResourceOverload,
        PerformanceDegradation
    }
}