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
    }

    public class ModelPerformanceMonitor : IModelPerformanceMonitor
    {
        private readonly ILogger<ModelPerformanceMonitor> _logger;
        private readonly MLModelValidationService _validationService;
        private readonly CacheMonitoringHub _monitoringHub;
        private readonly IAutomaticMitigationService _mitigationService;
        private readonly Dictionary<string, double> _performanceThresholds;
        private readonly List<PerformanceMetrics> _metricsHistory;

        public ModelPerformanceMonitor(
            ILogger<ModelPerformanceMonitor> logger,
            MLModelValidationService validationService,
            CacheMonitoringHub monitoringHub,
            IAutomaticMitigationService mitigationService)
        {
            _logger = logger;
            _validationService = validationService;
            _monitoringHub = monitoringHub;
            _mitigationService = mitigationService;
            _metricsHistory = new List<PerformanceMetrics>();
            
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
            return currentMetrics.Accuracy < _performanceThresholds["accuracy"] ||
                   currentMetrics.F1Score < _performanceThresholds["f1_score"] ||
                   currentMetrics.Precision < _performanceThresholds["precision"] ||
                   currentMetrics.Recall < _performanceThresholds["recall"];
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
}