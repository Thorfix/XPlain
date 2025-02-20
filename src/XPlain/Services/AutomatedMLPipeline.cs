using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace XPlain.Services
{
    public class ModelPerformanceMetrics
    {
        public double Accuracy { get; set; }
        public double DataDrift { get; set; }
        public double Latency { get; set; }
        public double ResourceUsage { get; set; }
        public Dictionary<string, double> CustomMetrics { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class RetrainingTrigger
    {
        public string Type { get; set; } // "performance", "schedule", "data_drift"
        public string Reason { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ModelDeploymentResult
    {
        public string ModelId { get; set; }
        public bool Success { get; set; }
        public string Status { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
        public List<string> Warnings { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AutomatedMLPipeline : BackgroundService
    {
        private readonly ILogger<AutomatedMLPipeline> _logger;
        private readonly MLModelTrainingService _trainingService;
        private readonly MLModelValidationService _validationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly Queue<ModelPerformanceMetrics> _performanceHistory;
        private readonly List<RetrainingTrigger> _retrainingHistory;
        private readonly Dictionary<string, double> _thresholds;
        private readonly TimeSpan _monitoringInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _scheduledRetrainingInterval = TimeSpan.FromDays(7);
        private DateTime _lastScheduledRetraining = DateTime.MinValue;

        public AutomatedMLPipeline(
            ILogger<AutomatedMLPipeline> logger,
            MLModelTrainingService trainingService,
            MLModelValidationService validationService,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _trainingService = trainingService;
            _validationService = validationService;
            _serviceProvider = serviceProvider;
            _performanceHistory = new Queue<ModelPerformanceMetrics>();
            _retrainingHistory = new List<RetrainingTrigger>();
            
            // Default thresholds
            _thresholds = new Dictionary<string, double>
            {
                ["accuracy_threshold"] = 0.85,
                ["drift_threshold"] = 0.15,
                ["latency_threshold"] = 200, // milliseconds
                ["resource_usage_threshold"] = 0.80, // 80% of allocated resources
                ["performance_window"] = 100 // number of predictions to analyze
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MonitorModelPerformance();
                    await CheckRetrainingTriggers();
                    await Task.Delay(_monitoringInterval, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in automated ML pipeline execution");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Back off on error
                }
            }
        }

        private async Task MonitorModelPerformance()
        {
            try
            {
                var activeModel = await _trainingService.GetActiveModelVersion();
                if (activeModel == null)
                {
                    _logger.LogWarning("No active model found during performance monitoring");
                    return;
                }

                var healthReport = await _trainingService.GenerateModelHealthReport();
                
                var metrics = new ModelPerformanceMetrics
                {
                    Accuracy = healthReport.Metrics["prediction_accuracy"],
                    DataDrift = healthReport.Metrics["data_drift_score"],
                    Latency = healthReport.Metrics["prediction_latency_ms"],
                    ResourceUsage = healthReport.Metrics.GetValueOrDefault("resource_usage", 0),
                    CustomMetrics = healthReport.Metrics,
                    Timestamp = DateTime.UtcNow
                };

                _performanceHistory.Enqueue(metrics);

                // Keep only recent history
                while (_performanceHistory.Count > 1000)
                {
                    _performanceHistory.Dequeue();
                }

                // Log metrics
                _logger.LogInformation(
                    "Model Performance Metrics - Accuracy: {Accuracy:F2}, DataDrift: {DataDrift:F2}, Latency: {Latency:F2}ms",
                    metrics.Accuracy,
                    metrics.DataDrift,
                    metrics.Latency);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring model performance");
            }
        }

        private async Task CheckRetrainingTriggers()
        {
            try
            {
                var activeModel = await _trainingService.GetActiveModelVersion();
                if (activeModel == null) return;

                var metrics = _performanceHistory.Count > 0 ? _performanceHistory.ToArray() : Array.Empty<ModelPerformanceMetrics>();
                if (metrics.Length == 0) return;

                var latestMetrics = metrics[^1];
                var trigger = await DetectRetrainingTrigger(metrics, activeModel);

                if (trigger != null)
                {
                    _retrainingHistory.Add(trigger);
                    await InitiateRetraining(trigger);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking retraining triggers");
            }
        }

        private async Task<RetrainingTrigger> DetectRetrainingTrigger(ModelPerformanceMetrics[] metrics, ModelVersion activeModel)
        {
            // Check accuracy degradation
            if (metrics[^1].Accuracy < _thresholds["accuracy_threshold"])
            {
                return new RetrainingTrigger
                {
                    Type = "performance",
                    Reason = "Accuracy below threshold",
                    Metrics = new Dictionary<string, double> { ["accuracy"] = metrics[^1].Accuracy },
                    Timestamp = DateTime.UtcNow
                };
            }

            // Check data drift
            if (metrics[^1].DataDrift > _thresholds["drift_threshold"])
            {
                return new RetrainingTrigger
                {
                    Type = "data_drift",
                    Reason = "Significant data drift detected",
                    Metrics = new Dictionary<string, double> { ["drift"] = metrics[^1].DataDrift },
                    Timestamp = DateTime.UtcNow
                };
            }

            // Check scheduled retraining
            if (DateTime.UtcNow - _lastScheduledRetraining > _scheduledRetrainingInterval)
            {
                return new RetrainingTrigger
                {
                    Type = "schedule",
                    Reason = "Scheduled retraining interval reached",
                    Metrics = new Dictionary<string, double>(),
                    Timestamp = DateTime.UtcNow
                };
            }

            return null;
        }

        private async Task<ModelDeploymentResult> InitiateRetraining(RetrainingTrigger trigger)
        {
            try
            {
                _logger.LogInformation(
                    "Initiating model retraining - Trigger: {TriggerType}, Reason: {Reason}",
                    trigger.Type,
                    trigger.Reason);

                // Create training parameters based on trigger type
                var parameters = new ModelTrainingParameters
                {
                    IncludeTimeFeatures = true,
                    IncludeUserActivityPatterns = true,
                    IncludeQueryPatterns = true,
                    OptimizeForResourceUsage = trigger.Type == "performance"
                };

                // Train new model
                var newModel = await _trainingService.TrainModelAsync(
                    await _trainingService.GetTrainingData(),
                    parameters);

                // Start shadow deployment
                await _validationService.StartShadowDeploymentAsync(newModel.Id, newModel.Model);

                // Monitor shadow deployment
                var deploymentResult = await MonitorShadowDeployment(newModel.Id);

                if (deploymentResult.Success)
                {
                    // Activate new model if validation successful
                    await _trainingService.ActivateModelVersion(newModel.Id);
                    _lastScheduledRetraining = DateTime.UtcNow;
                }
                else
                {
                    _logger.LogWarning(
                        "Shadow deployment validation failed for model {ModelId}: {Status}",
                        newModel.Id,
                        deploymentResult.Status);
                }

                return deploymentResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during model retraining process");
                return new ModelDeploymentResult
                {
                    Success = false,
                    Status = "Error: " + ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        private async Task<ModelDeploymentResult> MonitorShadowDeployment(string modelId)
        {
            var maxAttempts = 12; // Monitor for up to 1 hour (12 * 5 minutes)
            var attempt = 0;
            var warnings = new List<string>();

            while (attempt++ < maxAttempts)
            {
                try
                {
                    if (await _validationService.ShouldPromoteModelAsync(modelId))
                    {
                        return new ModelDeploymentResult
                        {
                            ModelId = modelId,
                            Success = true,
                            Status = "Validation successful",
                            Metrics = (await _validationService.GetValidationStatisticsAsync())[modelId].ToDictionary(),
                            Warnings = warnings,
                            Timestamp = DateTime.UtcNow
                        };
                    }

                    if (await _validationService.ShouldRollbackModelAsync(modelId))
                    {
                        return new ModelDeploymentResult
                        {
                            ModelId = modelId,
                            Success = false,
                            Status = "Validation failed - model performance below requirements",
                            Metrics = (await _validationService.GetValidationStatisticsAsync())[modelId].ToDictionary(),
                            Warnings = warnings,
                            Timestamp = DateTime.UtcNow
                        };
                    }

                    // Collect any new warnings
                    var stats = await _validationService.GetValidationStatisticsAsync();
                    if (stats.ContainsKey(modelId))
                    {
                        warnings.AddRange(stats[modelId].RecentWarnings);
                    }

                    await Task.Delay(_monitoringInterval);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error monitoring shadow deployment for model {ModelId}", modelId);
                    warnings.Add($"Monitoring error: {ex.Message}");
                }
            }

            return new ModelDeploymentResult
            {
                ModelId = modelId,
                Success = false,
                Status = "Validation timeout - insufficient data collected",
                Warnings = warnings,
                Timestamp = DateTime.UtcNow
            };
        }

        public async Task UpdateThresholds(Dictionary<string, double> newThresholds)
        {
            foreach (var (key, value) in newThresholds)
            {
                if (_thresholds.ContainsKey(key))
                {
                    _thresholds[key] = value;
                }
            }
        }

        public async Task<IEnumerable<ModelPerformanceMetrics>> GetPerformanceHistory()
        {
            return _performanceHistory.ToArray();
        }

        public async Task<IEnumerable<RetrainingTrigger>> GetRetrainingHistory()
        {
            return _retrainingHistory.ToArray();
        }
    }
}