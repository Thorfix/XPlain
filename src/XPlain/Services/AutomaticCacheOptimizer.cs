using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class AutomaticCacheOptimizer : IAutomaticCacheOptimizer
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly ICacheMonitoringService _monitoringService;
        private readonly IMLModelTrainingService _modelTrainingService;
        private readonly MLPredictionService _predictionService;
        private readonly MLModelValidationService _validationService;
        private readonly ILogger<AutomaticCacheOptimizer> _logger;
        private readonly Dictionary<string, double> _baselineThresholds;
        private readonly string _strategyHistoryPath = "optimization_strategies.json";
        private readonly string _auditTrailPath = "optimization_audit.json";
        private readonly double _maxThresholdAdjustment = 0.2; // Maximum 20% adjustment
        private readonly TimeSpan _optimizationCooldown = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _rollbackCheckPeriod = TimeSpan.FromMinutes(1);
        private DateTime _lastOptimization = DateTime.MinValue;
        private readonly Dictionary<string, OptimizationStrategy> _optimizationStrategies;
        private readonly Dictionary<string, OptimizationAction> _activeOptimizations;
        private readonly Queue<OptimizationAction> _auditTrail;
        private readonly int _maxAuditTrailSize = 1000;
        private bool _emergencyOverrideActive;

        public AutomaticCacheOptimizer(
            ICacheProvider cacheProvider,
            ICacheMonitoringService monitoringService,
            IMLModelTrainingService modelTrainingService,
            MLPredictionService predictionService,
            MLModelValidationService validationService,
            ILogger<AutomaticCacheOptimizer> logger)
        {
            _cacheProvider = cacheProvider;
            _monitoringService = monitoringService;
            _logger = logger;
            _modelTrainingService = modelTrainingService;
            _baselineThresholds = new Dictionary<string, double>();
            _optimizationStrategies = LoadStrategies();
            _activeOptimizations = new Dictionary<string, OptimizationAction>();
            _auditTrail = LoadAuditTrail();

            // Start optimization monitoring
            _ = MonitorOptimizationsAsync();
            _ = PeriodicStrategyPersistenceAsync();
        }

        private Dictionary<string, OptimizationStrategy> LoadStrategies()
        {
            try
            {
                if (File.Exists(_strategyHistoryPath))
                {
                    var json = File.ReadAllText(_strategyHistoryPath);
                    return JsonSerializer.Deserialize<Dictionary<string, OptimizationStrategy>>(json) 
                           ?? new Dictionary<string, OptimizationStrategy>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading optimization strategies");
            }
            return new Dictionary<string, OptimizationStrategy>();
        }

        private Queue<OptimizationAction> LoadAuditTrail()
        {
            try
            {
                if (File.Exists(_auditTrailPath))
                {
                    var json = File.ReadAllText(_auditTrailPath);
                    var list = JsonSerializer.Deserialize<List<OptimizationAction>>(json) ?? new List<OptimizationAction>();
                    return new Queue<OptimizationAction>(list);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading audit trail");
            }
            return new Queue<OptimizationAction>();
        }

        private async Task PeriodicStrategyPersistenceAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5)); // Save every 5 minutes
                await SaveStrategiesAsync();
                await SaveAuditTrailAsync();
            }
        }

        private async Task SaveStrategiesAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_optimizationStrategies, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(_strategyHistoryPath, json);
                _logger.LogInformation("Saved optimization strategies to disk");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving optimization strategies");
            }
        }

        private async Task SaveAuditTrailAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_auditTrail.ToList(), new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(_auditTrailPath, json);
                _logger.LogInformation("Saved audit trail to disk");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving audit trail");
            }
        }

        public async Task OptimizeAsync(PredictionResult prediction)
        {
            if (DateTime.UtcNow - _lastOptimization < _optimizationCooldown)
            {
                return;
            }

            if (prediction.Confidence < 0.7)
            {
                _logger.LogInformation("Skipping optimization due to low prediction confidence: {Confidence}", prediction.Confidence);
                return;
            }

            try
            {
                var currentMetrics = await _monitoringService.GetPerformanceMetricsAsync();
                var currentSize = await _cacheProvider.GetCacheSizeAsync();
                var maxSize = await _cacheProvider.GetMaxCacheSizeAsync();

                // Determine optimization strategy based on prediction
                if (prediction.DetectedPattern != null)
                {
                    switch (prediction.DetectedPattern.Type)
                    {
                        case "LowHitRate":
                            await HandleLowHitRateAsync(currentMetrics, prediction);
                            break;
                        case "HighMemoryUsage":
                            await HandleHighMemoryUsageAsync(currentSize, maxSize, prediction);
                            break;
                        case "SlowResponse":
                            await HandleSlowResponseAsync(currentMetrics, prediction);
                            break;
                    }
                }

                _lastOptimization = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache optimization");
            }
        }

        public async Task AdjustEvictionPolicyAsync(TrendAnalysis trends)
        {
            try
            {
                var currentPolicy = await _cacheProvider.GetEvictionPolicyAsync();
                ICacheEvictionPolicy newPolicy = currentPolicy;

                switch (trends.Trend)
                {
                    case TrendDirection.Increasing when trends.Volatility > 0.5:
                        // High volatility with increasing trend - use LRU for quick adaptation
                        newPolicy = new LRUEvictionPolicy();
                        break;
                    case TrendDirection.Stable when trends.Seasonality > 0.7:
                        // Strong seasonality - use LFU to maintain frequently accessed items
                        newPolicy = new LFUEvictionPolicy();
                        break;
                    case TrendDirection.Decreasing:
                        // Decreasing trend - use FIFO to maintain fresh data
                        newPolicy = new FIFOEvictionPolicy();
                        break;
                }

                if (newPolicy.GetType() != currentPolicy.GetType())
                {
                    await _cacheProvider.SetEvictionPolicyAsync(newPolicy);
                    _logger.LogInformation(
                        "Adjusted eviction policy from {OldPolicy} to {NewPolicy}",
                        currentPolicy.GetType().Name,
                        newPolicy.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adjusting eviction policy");
            }
        }

        public async Task UpdateCacheWarningThresholdsAsync(List<PredictedAlert> alerts)
        {
            if (!alerts.Any()) return;

            try
            {
                var currentThresholds = await _monitoringService.GetCurrentThresholdsAsync();

                // Store baseline thresholds if not already stored
                if (!_baselineThresholds.Any())
                {
                    _baselineThresholds["MinHitRatio"] = currentThresholds.MinHitRatio;
                    _baselineThresholds["MaxMemoryUsageMB"] = currentThresholds.MaxMemoryUsageMB;
                    _baselineThresholds["MaxResponseTimeMs"] = currentThresholds.MaxResponseTimeMs;
                }

                foreach (var alert in alerts.Where(a => a.Confidence > 0.8))
                {
                    double adjustment = DetermineThresholdAdjustment(alert);
                    
                    switch (alert.Metric)
                    {
                        case "CacheHitRate":
                            currentThresholds.MinHitRatio = AdjustThreshold(
                                currentThresholds.MinHitRatio,
                                _baselineThresholds["MinHitRatio"],
                                adjustment);
                            break;
                        case "MemoryUsage":
                            currentThresholds.MaxMemoryUsageMB = AdjustThreshold(
                                currentThresholds.MaxMemoryUsageMB,
                                _baselineThresholds["MaxMemoryUsageMB"],
                                adjustment);
                            break;
                        case "AverageResponseTime":
                            currentThresholds.MaxResponseTimeMs = AdjustThreshold(
                                currentThresholds.MaxResponseTimeMs,
                                _baselineThresholds["MaxResponseTimeMs"],
                                adjustment);
                            break;
                    }
                }

                await _monitoringService.UpdateThresholdsAsync(currentThresholds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cache warning thresholds");
            }
        }

        public async Task PrewarmFrequentItemsAsync(Dictionary<string, double> hitRates)
        {
            try
            {
                var frequentItems = hitRates
                    .Where(hr => hr.Value > 0.8) // Items with high hit rates
                    .Select(hr => hr.Key)
                    .ToList();

                if (!frequentItems.Any())
                {
                    return;
                }

                var currentSize = await _cacheProvider.GetCacheSizeAsync();
                var maxSize = await _cacheProvider.GetMaxCacheSizeAsync();
                var availableSpace = maxSize - currentSize;

                if (availableSpace > maxSize * 0.1) // Ensure at least 10% free space
                {
                    foreach (var item in frequentItems)
                    {
                        await _cacheProvider.PrewarmItemAsync(item);
                    }
                    
                    _logger.LogInformation(
                        "Prewarmed {Count} frequent items in cache",
                        frequentItems.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error prewarming frequent items");
            }
        }

        private async Task HandleLowHitRateAsync(
            Dictionary<string, double> currentMetrics,
            PredictionResult prediction)
        {
            var hitRate = currentMetrics["CacheHitRate"];
            if (hitRate < 0.6) // Critical low hit rate
            {
                await _cacheProvider.ExpandCacheSizeAsync(0.2); // Increase by 20%
                _logger.LogInformation("Increased cache size by 20% due to low hit rate prediction");
            }
            else if (prediction.TimeToImpact < TimeSpan.FromMinutes(15))
            {
                await _cacheProvider.ExpandCacheSizeAsync(0.1); // Increase by 10%
                _logger.LogInformation("Proactively increased cache size by 10%");
            }
        }

        private async Task HandleHighMemoryUsageAsync(
            long currentSize,
            long maxSize,
            PredictionResult prediction)
        {
            var usageRatio = (double)currentSize / maxSize;
            if (usageRatio > 0.9) // Critical high memory usage
            {
                await _cacheProvider.TrimCacheAsync(0.2); // Reduce by 20%
                _logger.LogInformation("Reduced cache size by 20% due to high memory usage");
            }
            else if (prediction.TimeToImpact < TimeSpan.FromMinutes(15))
            {
                await _cacheProvider.TrimCacheAsync(0.1); // Reduce by 10%
                _logger.LogInformation("Proactively reduced cache size by 10%");
            }
        }

        private async Task HandleSlowResponseAsync(
            Dictionary<string, double> currentMetrics,
            PredictionResult prediction)
        {
            var responseTime = currentMetrics["AverageResponseTime"];
            if (responseTime > 1000) // Response time > 1 second
            {
                await _cacheProvider.OptimizeIndexesAsync();
                _logger.LogInformation("Optimized cache indexes due to slow response time");
            }
            else if (prediction.TimeToImpact < TimeSpan.FromMinutes(15))
            {
                await _cacheProvider.PrewarmCacheAsync();
                _logger.LogInformation("Proactively prewarmed cache");
            }
        }

        private double DetermineThresholdAdjustment(PredictedAlert alert)
        {
            var severityMultiplier = alert.Severity switch
            {
                "Critical" => 0.2,
                "Warning" => 0.1,
                _ => 0.05
            };

            var urgencyMultiplier = alert.TimeToImpact < TimeSpan.FromMinutes(15) ? 1.5 : 1.0;
            
            return severityMultiplier * urgencyMultiplier;
        }

        private double AdjustThreshold(double current, double baseline, double adjustment)
        {
            var maxAdjustment = baseline * _maxThresholdAdjustment;
            var proposedAdjustment = current * adjustment;
            var limitedAdjustment = Math.Min(Math.Abs(proposedAdjustment), maxAdjustment) 
                                  * Math.Sign(proposedAdjustment);
            
            return Math.Max(baseline * 0.5, Math.Min(baseline * 1.5, current + limitedAdjustment));
        }

        private async Task MonitorOptimizationsAsync()
        {
            while (true)
            {
                await Task.Delay(_rollbackCheckPeriod);
                
                try
                {
                    if (_emergencyOverrideActive) continue;

                    var currentMetrics = await _monitoringService.GetPerformanceMetricsAsync();
                    var activeOptimizations = _activeOptimizations.ToList();

                    foreach (var (key, action) in activeOptimizations)
                    {
                        if (ShouldRollback(action, currentMetrics))
                        {
                            await RollbackOptimizationAsync(action);
                            UpdateOptimizationStrategy(action, false);
                            _activeOptimizations.Remove(key);
                        }
                        else if (DateTime.UtcNow - action.Timestamp > TimeSpan.FromMinutes(15))
                        {
                            // Optimization successful after 15 minutes
                            UpdateOptimizationStrategy(action, true);
                            _activeOptimizations.Remove(key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in optimization monitoring");
                }
            }
        }

        private bool ShouldRollback(OptimizationAction action, Dictionary<string, double> currentMetrics)
        {
            // Check if metrics degraded significantly after optimization
            foreach (var (metric, valueBefore) in action.MetricsBefore)
            {
                if (!currentMetrics.ContainsKey(metric)) continue;
                
                var valueAfter = currentMetrics[metric];
                var degradation = metric.ToLower() switch
                {
                    "cachehitrate" => valueBefore - valueAfter,
                    "memoryusage" => valueAfter - valueBefore,
                    "averageresponsetime" => valueAfter - valueBefore,
                    _ => 0
                };

                if (degradation > valueBefore * 0.3) // 30% degradation threshold
                {
                    return true;
                }
            }

            return false;
        }

        private async Task RollbackOptimizationAsync(OptimizationAction action)
        {
            try
            {
                switch (action.ActionType)
                {
                    case "CacheSizeIncrease":
                        await _cacheProvider.TrimCacheAsync(action.MetricsBefore["cacheSize"]);
                        break;
                    case "CacheSizeDecrease":
                        await _cacheProvider.ExpandCacheSizeAsync(action.MetricsBefore["cacheSize"]);
                        break;
                    case "EvictionPolicyChange":
                        var previousPolicy = Enum.Parse<EvictionPolicyType>(action.MetricsBefore["evictionPolicy"].ToString());
                        await _cacheProvider.SetEvictionPolicyAsync(CreateEvictionPolicy(previousPolicy));
                        break;
                }

                _logger.LogWarning("Rolled back optimization: {ActionType} due to performance degradation", action.ActionType);

                // Update ML model with rollback data
                await UpdateMLModelWithOptimizationResult(action, false);
                
                RecordAuditTrail(new OptimizationAction
                {
                    ActionType = $"Rollback_{action.ActionType}",
                    Timestamp = DateTime.UtcNow,
                    MetricsBefore = action.MetricsAfter,
                    MetricsAfter = action.MetricsBefore,
                    WasSuccessful = true,
                    RollbackReason = "Performance degradation"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rolling back optimization: {ActionType}", action.ActionType);
            }
        }

        private async Task UpdateOptimizationStrategy(OptimizationAction action, bool wasSuccessful)
        {
            var key = $"{action.ActionType}_{action.Trigger}";
            if (!_optimizationStrategies.ContainsKey(key))
            {
                _optimizationStrategies[key] = new OptimizationStrategy
                {
                    Trigger = action.Trigger,
                    Action = action.ActionType,
                    SuccessRate = 0,
                    TotalAttempts = 0,
                    SuccessfulAttempts = 0
                };
            }

            var strategy = _optimizationStrategies[key];
            strategy.TotalAttempts++;
            if (wasSuccessful)
            {
                strategy.SuccessfulAttempts++;
            }
            strategy.SuccessRate = (double)strategy.SuccessfulAttempts / strategy.TotalAttempts;
            strategy.History.Add(action);

            // Trim history if too long
            if (strategy.History.Count > 100)
            {
                strategy.History.RemoveAt(0);
            }

            // Update ML model with optimization outcome
            await UpdateMLModelWithOptimizationResult(action, wasSuccessful);
        }

        private async Task UpdateMLModelWithOptimizationResult(OptimizationAction action, bool wasSuccessful)
        {
            try
            {
                // Create training data from optimization outcome
                var trainingData = new MLModelTrainingData
                {
                    Timestamp = action.Timestamp,
                    ActionType = action.ActionType,
                    MetricsBefore = action.MetricsBefore,
                    MetricsAfter = action.MetricsAfter,
                    WasSuccessful = wasSuccessful,
                    OptimizationImpact = CalculateOptimizationImpact(action),
                    Context = new OptimizationContext
                    {
                        CacheSize = await _cacheProvider.GetCacheSizeAsync(),
                        EvictionPolicy = await _cacheProvider.GetEvictionPolicyAsync(),
                        WorkloadCharacteristics = await _monitoringService.GetWorkloadCharacteristicsAsync()
                    }
                };

                // Add to training dataset and force retraining
                await _modelTrainingService.AddTrainingDataAsync(trainingData);
                
                // Always retrain after successful optimizations to quickly adapt
                if (wasSuccessful)
                {
                    await _modelTrainingService.TrainModelAsync(action.ActionType);
                    _logger.LogInformation("Retrained ML model for {ActionType} based on successful optimization", action.ActionType);
                    
                    // Update prediction models immediately
                    var newModel = await _modelTrainingService.GetLatestModel(action.ActionType);
                    if (newModel != null && await _modelTrainingService.ValidateModel(newModel, action.ActionType))
                    {
                        await _predictionService.UpdateModelAsync(action.ActionType, newModel);
                        _logger.LogInformation("Updated prediction model for {ActionType} with newly trained version", action.ActionType);
                    }
                }
                // For failed optimizations, retrain periodically to adapt to changing patterns
                else if (ShouldRetrainModel(action.ActionType))
                {
                    await _modelTrainingService.TrainModelAsync(action.ActionType);
                    _logger.LogInformation("Retrained ML model for {ActionType} based on optimization outcomes", action.ActionType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ML model with optimization result");
            }
        }

        private double CalculateOptimizationImpact(OptimizationAction action)
        {
            double impact = 0;
            foreach (var (metric, valueBefore) in action.MetricsBefore)
            {
                if (!action.MetricsAfter.ContainsKey(metric)) continue;

                var valueAfter = action.MetricsAfter[metric];
                impact += metric.ToLower() switch
                {
                    "cachehitrate" => (valueAfter - valueBefore) / valueBefore,
                    "memoryusage" => (valueBefore - valueAfter) / valueBefore, // Lower is better
                    "averageresponsetime" => (valueBefore - valueAfter) / valueBefore, // Lower is better
                    _ => 0
                };
            }
            return impact / action.MetricsBefore.Count; // Average impact across all metrics
        }

        private bool ShouldRetrainModel(string actionType)
        {
            if (!_optimizationStrategies.ContainsKey(actionType)) return false;
            
            var strategy = _optimizationStrategies[actionType];
            // Retrain after every 10 new optimization attempts
            return strategy.TotalAttempts % 10 == 0;
        }

        private void RecordAuditTrail(OptimizationAction action)
        {
            _auditTrail.Enqueue(action);
            while (_auditTrail.Count > _maxAuditTrailSize)
            {
                _auditTrail.Dequeue();
            }
        }

        private ICacheEvictionPolicy CreateEvictionPolicy(EvictionPolicyType policyType)
        {
            return policyType switch
            {
                EvictionPolicyType.LRU => new LRUEvictionPolicy(),
                EvictionPolicyType.LFU => new LFUEvictionPolicy(),
                EvictionPolicyType.FIFO => new FIFOEvictionPolicy(),
                _ => throw new ArgumentException($"Unknown policy type: {policyType}")
            };
        }

        public async Task<OptimizationMetrics> GetOptimizationMetricsAsync()
        {
            return new OptimizationMetrics
            {
                ActiveOptimizations = _activeOptimizations.Values.ToList(),
                SuccessRateByStrategy = _optimizationStrategies.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.SuccessRate
                ),
                RecentActions = _auditTrail.TakeLast(50).ToList(),
                EmergencyOverrideActive = _emergencyOverrideActive
            };
        }

        public async Task SetEmergencyOverrideAsync(bool enabled)
        {
            _emergencyOverrideActive = enabled;
            _logger.LogWarning("Emergency override {Status}", enabled ? "activated" : "deactivated");
            
            if (enabled)
            {
                // Rollback all active optimizations
                foreach (var action in _activeOptimizations.Values)
                {
                    await RollbackOptimizationAsync(action);
                }
                _activeOptimizations.Clear();
            }
        }
    }

    public class OptimizationMetrics
    {
        public List<OptimizationAction> ActiveOptimizations { get; set; }
        public Dictionary<string, double> SuccessRateByStrategy { get; set; }
        public List<OptimizationAction> RecentActions { get; set; }
        public bool EmergencyOverrideActive { get; set; }
    }

    public enum EvictionPolicyType
    {
        LRU,
        LFU,
        FIFO
    }
}