using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class OptimizationHistory
    {
        public string EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
        public Dictionary<string, object> Actions { get; set; } = new();
    }

    public class OptimizationMetrics
    {
        public int TotalOptimizationAttempts { get; set; }
        public int SuccessfulOptimizations { get; set; }
        public double SuccessRate => TotalOptimizationAttempts > 0 
            ? (double)SuccessfulOptimizations / TotalOptimizationAttempts 
            : 0;
        public int PolicySwitches { get; set; }
        public int ThresholdAdjustments { get; set; }
        public int PrewarmAttempts { get; set; }
        public int EmergencyActions { get; set; }
        public DateTime LastOptimizationTime { get; set; }
        public bool EmergencyOverrideEnabled { get; set; }
        public string CurrentCacheStrategy { get; set; }
        public Dictionary<string, double> PerformanceImprovements { get; set; } = new();
    }

    public class AutomaticCacheOptimizer : IAutomaticCacheOptimizer
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly ICacheMonitoringService _monitoringService;
        private readonly ILogger<AutomaticCacheOptimizer> _logger;
        private readonly MLPredictionService _predictionService;
        private readonly List<OptimizationHistory> _history = new();
        private readonly OptimizationMetrics _metrics = new();
        private bool _emergencyOverrideEnabled;
        
        public AutomaticCacheOptimizer(
            ICacheProvider cacheProvider,
            ICacheMonitoringService monitoringService,
            MLPredictionService predictionService = null,
            ILogger<AutomaticCacheOptimizer> logger = null)
        {
            _cacheProvider = cacheProvider;
            _monitoringService = monitoringService;
            _predictionService = predictionService ?? new MLPredictionService();
            _logger = logger ?? new Logger<AutomaticCacheOptimizer>(new LoggerFactory());
            _metrics.LastOptimizationTime = DateTime.UtcNow;
            _metrics.CurrentCacheStrategy = "Default";
        }

        public async Task OptimizeAsync(PredictionResult prediction)
        {
            try
            {
                _metrics.TotalOptimizationAttempts++;
                _logger.LogInformation($"Optimizing cache based on prediction: {prediction.Value:F2} (confidence: {prediction.Confidence:F2})");
                
                var metrics = await _monitoringService.GetPerformanceMetricsAsync();
                
                // Record the optimization attempt
                var historyEntry = new OptimizationHistory
                {
                    EventType = "Optimization",
                    Timestamp = DateTime.UtcNow,
                    Metrics = new Dictionary<string, object>(
                        metrics.ToDictionary(m => m.Key, m => (object)m.Value)),
                    Actions = new Dictionary<string, object>
                    {
                        ["prediction_value"] = prediction.Value,
                        ["prediction_confidence"] = prediction.Confidence
                    }
                };
                
                // Apply optimizations based on the prediction
                if (prediction.Value < 0.3 && prediction.Confidence > 0.7)
                {
                    // Low performance predicted with high confidence
                    await _cacheProvider.UpdateEvictionPolicy(EvictionStrategy.LRU);
                    historyEntry.Actions["policy_switch"] = "LRU";
                    _metrics.PolicySwitches++;
                    _metrics.CurrentCacheStrategy = "LRU";
                }
                else if (prediction.Value > 0.7 && prediction.Confidence > 0.7)
                {
                    // High performance predicted with high confidence
                    await _cacheProvider.UpdateEvictionPolicy(EvictionStrategy.HitRateWeighted);
                    historyEntry.Actions["policy_switch"] = "HitRateWeighted";
                    _metrics.PolicySwitches++;
                    _metrics.CurrentCacheStrategy = "HitRateWeighted";
                }
                
                _history.Add(historyEntry);
                _metrics.SuccessfulOptimizations++;
                _metrics.LastOptimizationTime = DateTime.UtcNow;
                
                // Calculate performance improvement
                var newMetrics = await _monitoringService.GetPerformanceMetricsAsync();
                
                // Record improvement for hit rate
                if (metrics.TryGetValue("CacheHitRate", out var oldHitRate) && 
                    newMetrics.TryGetValue("CacheHitRate", out var newHitRate))
                {
                    var improvement = newHitRate - oldHitRate;
                    _metrics.PerformanceImprovements["hit_rate"] = improvement;
                }
                
                // Record improvement for response time
                if (metrics.TryGetValue("AverageResponseTime", out var oldResponseTime) && 
                    newMetrics.TryGetValue("AverageResponseTime", out var newResponseTime))
                {
                    var improvement = (oldResponseTime - newResponseTime) / oldResponseTime;
                    _metrics.PerformanceImprovements["response_time"] = improvement;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error optimizing cache");
            }
        }

        public async Task AdjustEvictionPolicyAsync(TrendAnalysis trends)
        {
            _logger.LogInformation($"Adjusting eviction policy based on trend: {trends.Trend}");
            
            var historyEntry = new OptimizationHistory
            {
                EventType = "EvictionPolicyAdjustment",
                Timestamp = DateTime.UtcNow,
                Metrics = new Dictionary<string, object>
                {
                    ["current_value"] = trends.CurrentValue,
                    ["projected_value"] = trends.ProjectedValue,
                    ["change_percent"] = trends.ChangePercent
                }
            };
            
            if (trends.Trend == "Decreasing" && trends.ChangePercent < -10)
            {
                // Significant downward trend
                if (_emergencyOverrideEnabled)
                {
                    await _cacheProvider.UpdateEvictionPolicy(EvictionStrategy.Adaptive);
                    historyEntry.Actions["policy_switch"] = "Adaptive";
                    _metrics.PolicySwitches++;
                    _metrics.EmergencyActions++;
                    _metrics.CurrentCacheStrategy = "Adaptive";
                }
            }
            else if (trends.Trend == "Increasing" && trends.ChangePercent > 10)
            {
                // Significant upward trend
                await _cacheProvider.UpdateEvictionPolicy(EvictionStrategy.HitRateWeighted);
                historyEntry.Actions["policy_switch"] = "HitRateWeighted";
                _metrics.PolicySwitches++;
                _metrics.CurrentCacheStrategy = "HitRateWeighted";
            }
            else if (trends.Trend == "Stable")
            {
                // Stable trend
                await _cacheProvider.UpdateEvictionPolicy(EvictionStrategy.LRU);
                historyEntry.Actions["policy_switch"] = "LRU";
                _metrics.PolicySwitches++;
                _metrics.CurrentCacheStrategy = "LRU";
            }
            
            _history.Add(historyEntry);
        }

        public async Task UpdateCacheWarningThresholdsAsync(List<PredictedAlert> alerts)
        {
            _logger.LogInformation($"Updating cache warning thresholds based on {alerts.Count} predicted alerts");
            
            var historyEntry = new OptimizationHistory
            {
                EventType = "ThresholdAdjustment",
                Timestamp = DateTime.UtcNow,
                Metrics = new Dictionary<string, object>
                {
                    ["alert_count"] = alerts.Count,
                    ["alert_types"] = alerts.Select(a => a.Type).ToList()
                }
            };
            
            var currentThresholds = await _monitoringService.GetCurrentThresholdsAsync();
            
            // Adjust thresholds based on predicted alerts
            foreach (var alert in alerts)
            {
                if (alert.Type == "CacheHitRate" && alert.Confidence > 0.8)
                {
                    currentThresholds.HitRateWarningThreshold = Math.Max(0.5, currentThresholds.HitRateWarningThreshold * 0.9);
                    currentThresholds.HitRateErrorThreshold = Math.Max(0.3, currentThresholds.HitRateErrorThreshold * 0.9);
                    historyEntry.Actions["adjusted_hit_rate_thresholds"] = true;
                }
                else if (alert.Type == "MemoryUsage" && alert.Confidence > 0.8)
                {
                    currentThresholds.MemoryUsageWarningThreshold = Math.Min(0.9, currentThresholds.MemoryUsageWarningThreshold * 1.1);
                    currentThresholds.MemoryUsageErrorThreshold = Math.Min(0.95, currentThresholds.MemoryUsageErrorThreshold * 1.05);
                    historyEntry.Actions["adjusted_memory_thresholds"] = true;
                }
                else if (alert.Type == "ResponseTime" && alert.Confidence > 0.8)
                {
                    currentThresholds.ResponseTimeWarningThresholdMs = (int)(currentThresholds.ResponseTimeWarningThresholdMs * 1.1);
                    currentThresholds.ResponseTimeErrorThresholdMs = (int)(currentThresholds.ResponseTimeErrorThresholdMs * 1.1);
                    historyEntry.Actions["adjusted_response_time_thresholds"] = true;
                }
            }
            
            await _monitoringService.UpdateMonitoringThresholdsAsync(currentThresholds);
            _metrics.ThresholdAdjustments++;
            
            _history.Add(historyEntry);
        }

        public async Task PrewarmFrequentItemsAsync(Dictionary<string, double> hitRates)
        {
            _logger.LogInformation($"Prewarming {hitRates.Count} frequent items");
            
            var historyEntry = new OptimizationHistory
            {
                EventType = "Prewarm",
                Timestamp = DateTime.UtcNow,
                Metrics = new Dictionary<string, object>
                {
                    ["item_count"] = hitRates.Count,
                    ["top_items"] = hitRates.OrderByDescending(h => h.Value).Take(5).Select(h => h.Key).ToList()
                }
            };
            
            int successCount = 0;
            foreach (var (key, hitRate) in hitRates.OrderByDescending(h => h.Value).Take(10))
            {
                var priority = hitRate switch
                {
                    > 0.8 => PreWarmPriority.Critical,
                    > 0.6 => PreWarmPriority.High,
                    > 0.4 => PreWarmPriority.Medium,
                    _ => PreWarmPriority.Low
                };
                
                if (await _cacheProvider.PreWarmKey(key, priority))
                {
                    successCount++;
                }
            }
            
            historyEntry.Actions["success_count"] = successCount;
            _history.Add(historyEntry);
            _metrics.PrewarmAttempts++;
        }

        public async Task<OptimizationMetrics> GetOptimizationMetricsAsync()
        {
            return _metrics;
        }

        public async Task SetEmergencyOverrideAsync(bool enabled)
        {
            _emergencyOverrideEnabled = enabled;
            _metrics.EmergencyOverrideEnabled = enabled;
            
            _logger.LogInformation($"Emergency override {(enabled ? "enabled" : "disabled")}");
            
            if (enabled)
            {
                // Apply emergency optimizations
                await _cacheProvider.UpdateEvictionPolicy(EvictionStrategy.Adaptive);
                _metrics.EmergencyActions++;
                _metrics.CurrentCacheStrategy = "Adaptive (Emergency)";
                
                var currentThresholds = await _monitoringService.GetCurrentThresholdsAsync();
                currentThresholds.MemoryUsageWarningThreshold = 0.75;
                currentThresholds.MemoryUsageErrorThreshold = 0.9;
                await _monitoringService.UpdateMonitoringThresholdsAsync(currentThresholds);
                
                _history.Add(new OptimizationHistory
                {
                    EventType = "EmergencyOverride",
                    Timestamp = DateTime.UtcNow,
                    Actions = new Dictionary<string, object>
                    {
                        ["policy_switch"] = "Adaptive",
                        ["memory_warning_threshold"] = 0.75,
                        ["memory_error_threshold"] = 0.9
                    }
                });
            }
        }
    }
}