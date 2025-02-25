using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{

    public class ModelPerformanceMonitor : IModelPerformanceMonitor
    {
        private readonly ILogger<ModelPerformanceMonitor> _logger;
        private readonly Dictionary<string, ModelMetrics> _modelMetrics = new();
        private readonly List<ModelAlert> _activeAlerts = new();
        private readonly TimeSeriesMetricsStore _metricsStore;
        private readonly MetricsSettings _settings;

        public ModelPerformanceMonitor(
            ILogger<ModelPerformanceMonitor> logger = null,
            IOptions<MetricsSettings> settings = null,
            TimeSeriesMetricsStore metricsStore = null)
        {
            _logger = logger ?? new Logger<ModelPerformanceMonitor>(new LoggerFactory());
            _settings = settings?.Value ?? new MetricsSettings();
            _metricsStore = metricsStore ?? new TimeSeriesMetricsStore();
        }

        public async Task TrackResponseAsync(string provider, string model, double responseTime, bool success, int tokenCount)
        {
            var key = $"{provider}:{model}";
            
            if (!_modelMetrics.TryGetValue(key, out var metrics))
            {
                metrics = new ModelMetrics();
                _modelMetrics[key] = metrics;
            }
            
            // Update metrics
            metrics.Update(responseTime, success, tokenCount);
            
            // Store time series data
            await _metricsStore.RecordMetricAsync($"{key}:response_time", responseTime);
            await _metricsStore.RecordMetricAsync($"{key}:success_rate", success ? 1.0 : 0.0);
            await _metricsStore.RecordMetricAsync($"{key}:token_count", tokenCount);
            
            // Check for alerts
            await CheckForAlertsAsync(provider, model, metrics);
        }

        private async Task CheckForAlertsAsync(string provider, string model, ModelMetrics metrics)
        {
            // Check response time alert
            if (metrics.AvgResponseTime > _settings.ResponseTimeAlertThresholdMs)
            {
                await CreateAlertAsync(
                    provider,
                    model,
                    "ResponseTime",
                    $"High response time detected for {provider}/{model}: {metrics.AvgResponseTime:F1}ms",
                    "Warning",
                    new Dictionary<string, object>
                    {
                        ["avg_response_time"] = metrics.AvgResponseTime,
                        ["threshold"] = _settings.ResponseTimeAlertThresholdMs
                    });
            }
            
            // Check error rate alert
            var errorRate = 1.0 - metrics.SuccessRate;
            if (errorRate > _settings.ErrorRateAlertThreshold)
            {
                await CreateAlertAsync(
                    provider,
                    model,
                    "ErrorRate",
                    $"High error rate detected for {provider}/{model}: {errorRate:P2}",
                    errorRate > _settings.ErrorRateAlertThreshold * 1.5 ? "Error" : "Warning",
                    new Dictionary<string, object>
                    {
                        ["error_rate"] = errorRate,
                        ["threshold"] = _settings.ErrorRateAlertThreshold
                    });
            }
        }

        private async Task CreateAlertAsync(
            string provider,
            string model,
            string type,
            string message,
            string severity,
            Dictionary<string, object> metadata = null)
        {
            // Check if we already have a similar active alert
            var existingAlert = _activeAlerts.FirstOrDefault(a => 
                a.Provider == provider && 
                a.Model == model && 
                a.Type == type);
                
            if (existingAlert != null)
            {
                // Update existing alert if it's older than 10 minutes
                if (DateTime.UtcNow - existingAlert.CreatedAt > TimeSpan.FromMinutes(10))
                {
                    existingAlert.Message = message;
                    existingAlert.Severity = severity;
                    existingAlert.CreatedAt = DateTime.UtcNow;
                    existingAlert.Metadata = metadata ?? new Dictionary<string, object>();
                }
                return;
            }
            
            // Create new alert
            _activeAlerts.Add(new ModelAlert
            {
                Provider = provider,
                Model = model,
                Type = type,
                Message = message,
                Severity = severity,
                Metadata = metadata ?? new Dictionary<string, object>()
            });
            
            // Limit number of active alerts
            if (_activeAlerts.Count > 100)
            {
                _activeAlerts.RemoveAt(0);
            }
        }

        public async Task<Dictionary<string, double>> GetModelMetricsAsync(string provider, string model)
        {
            var key = $"{provider}:{model}";
            
            if (!_modelMetrics.TryGetValue(key, out var metrics))
            {
                return new Dictionary<string, double>();
            }
            
            return new Dictionary<string, double>
            {
                ["avg_response_time"] = metrics.AvgResponseTime,
                ["success_rate"] = metrics.SuccessRate,
                ["avg_token_count"] = metrics.AvgTokenCount,
                ["request_count"] = metrics.RequestCount,
                ["error_count"] = metrics.ErrorCount
            };
        }

        public async Task<Dictionary<string, List<TimeSeriesDataPoint>>> GetHistoricalMetricsAsync(
            string provider, 
            string model, 
            TimeSpan period)
        {
            var key = $"{provider}:{model}";
            var result = new Dictionary<string, List<TimeSeriesDataPoint>>();
            
            var metrics = new[] { "response_time", "success_rate", "token_count" };
            
            foreach (var metric in metrics)
            {
                var data = await _metricsStore.GetMetricHistoryAsync($"{key}:{metric}", period);
                result[metric] = data.Select(p => new TimeSeriesDataPoint
                {
                    Timestamp = p.Timestamp,
                    Value = p.Value
                }).ToList();
            }
            
            return result;
        }

        public async Task<List<ModelAlert>> GetActiveAlertsAsync()
        {
            return _activeAlerts;
        }

        private class ModelMetrics
        {
            public double AvgResponseTime { get; private set; }
            public double SuccessRate { get; private set; } = 1.0;
            public double AvgTokenCount { get; private set; }
            public int RequestCount { get; private set; }
            public int ErrorCount { get; private set; }
            
            private double _totalResponseTime;
            private double _totalTokenCount;
            
            public void Update(double responseTime, bool success, int tokenCount)
            {
                RequestCount++;
                _totalResponseTime += responseTime;
                _totalTokenCount += tokenCount;
                
                if (!success)
                {
                    ErrorCount++;
                }
                
                AvgResponseTime = _totalResponseTime / RequestCount;
                SuccessRate = (RequestCount - ErrorCount) / (double)RequestCount;
                AvgTokenCount = _totalTokenCount / RequestCount;
            }
        }
    }
}