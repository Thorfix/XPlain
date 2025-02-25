using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public interface IModelPerformanceMonitor
    {
        Task<ModelPerformanceReport> GetPerformanceReportAsync();
        Task RecordMetricAsync(string metricName, double value);
        Task<Dictionary<string, double>> GetCurrentMetricsAsync();
    }

    public class ModelPerformanceReport
    {
        public double Accuracy { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double F1Score { get; set; }
        public double LatencyMs { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public Dictionary<string, double> CustomMetrics { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class ModelPerformanceMonitor : IModelPerformanceMonitor
    {
        private readonly ILogger<ModelPerformanceMonitor> _logger;
        private readonly AlertSettings _alertSettings;
        private readonly IAlertManagementService _alertService;
        private readonly Dictionary<string, double> _currentMetrics = new();
        private readonly object _metricsLock = new();

        public ModelPerformanceMonitor(
            ILogger<ModelPerformanceMonitor> logger,
            IOptions<AlertSettings> alertSettings,
            IAlertManagementService alertService)
        {
            _logger = logger;
            _alertSettings = alertSettings.Value;
            _alertService = alertService;
            
            // Initialize with default values
            _currentMetrics["accuracy"] = 1.0;
            _currentMetrics["precision"] = 1.0;
            _currentMetrics["recall"] = 1.0;
            _currentMetrics["f1_score"] = 1.0;
            _currentMetrics["latency_ms"] = 100;
            _currentMetrics["success_rate"] = 1.0;
        }

        public Task<ModelPerformanceReport> GetPerformanceReportAsync()
        {
            lock (_metricsLock)
            {
                var report = new ModelPerformanceReport
                {
                    Accuracy = GetMetric("accuracy"),
                    Precision = GetMetric("precision"),
                    Recall = GetMetric("recall"),
                    F1Score = GetMetric("f1_score"),
                    LatencyMs = GetMetric("latency_ms"),
                    TotalRequests = (int)GetMetric("total_requests", 0),
                    SuccessfulRequests = (int)GetMetric("successful_requests", 0),
                    CustomMetrics = new Dictionary<string, double>(_currentMetrics),
                    GeneratedAt = DateTime.UtcNow
                };
                
                return Task.FromResult(report);
            }
        }

        public Task RecordMetricAsync(string metricName, double value)
        {
            if (string.IsNullOrEmpty(metricName))
            {
                throw new ArgumentException("Metric name cannot be null or empty", nameof(metricName));
            }

            lock (_metricsLock)
            {
                _currentMetrics[metricName] = value;
                
                // Update derived metrics
                if (metricName == "total_requests" || metricName == "successful_requests")
                {
                    var total = GetMetric("total_requests", 0);
                    var successful = GetMetric("successful_requests", 0);
                    
                    if (total > 0)
                    {
                        _currentMetrics["success_rate"] = successful / total;
                    }
                }
                
                // Check for performance degradation and generate alerts
                CheckThresholdsAndAlert(metricName, value);
            }
            
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, double>> GetCurrentMetricsAsync()
        {
            lock (_metricsLock)
            {
                return Task.FromResult(new Dictionary<string, double>(_currentMetrics));
            }
        }

        private double GetMetric(string name, double defaultValue = 0)
        {
            return _currentMetrics.TryGetValue(name, out var value) ? value : defaultValue;
        }

        private void CheckThresholdsAndAlert(string metricName, double value)
        {
            switch (metricName)
            {
                case "accuracy" when value < _alertSettings.ModelPerformance.AccuracyThreshold:
                    CreatePerformanceAlert("Accuracy", value, _alertSettings.ModelPerformance.AccuracyThreshold);
                    break;
                    
                case "precision" when value < _alertSettings.ModelPerformance.PrecisionThreshold:
                    CreatePerformanceAlert("Precision", value, _alertSettings.ModelPerformance.PrecisionThreshold);
                    break;
                    
                case "recall" when value < _alertSettings.ModelPerformance.RecallThreshold:
                    CreatePerformanceAlert("Recall", value, _alertSettings.ModelPerformance.RecallThreshold);
                    break;
                    
                case "f1_score" when value < _alertSettings.ModelPerformance.F1ScoreThreshold:
                    CreatePerformanceAlert("F1 Score", value, _alertSettings.ModelPerformance.F1ScoreThreshold);
                    break;
                    
                case "success_rate" when value < 0.9:
                    var severity = value < 0.8 ? AlertSeverity.Critical : AlertSeverity.High;
                    CreateAlert(
                        $"Low Success Rate: {value:P2}",
                        $"The model success rate has dropped to {value:P2}",
                        severity);
                    break;
            }
        }

        private void CreatePerformanceAlert(string metricName, double value, double threshold)
        {
            var severity = value < threshold * 0.9 ? AlertSeverity.Critical : AlertSeverity.High;
            
            CreateAlert(
                $"Low {metricName}: {value:F3}",
                $"The model {metricName.ToLower()} has dropped to {value:F3}, which is below the threshold of {threshold:F3}",
                severity);
        }

        private void CreateAlert(string title, string description, AlertSeverity severity)
        {
            var alert = new Alert
            {
                Title = title,
                Description = description,
                Severity = severity,
                Source = "ModelPerformanceMonitor",
                Metadata = new Dictionary<string, string>
                {
                    ["current_metrics"] = JsonSerializer.Serialize(_currentMetrics)
                }
            };
            
            _alertService.CreateAlertAsync(alert);
        }
    }
}