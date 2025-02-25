using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace XPlain.Services
{
    // Required supporting classes for CacheMonitoringService
public class PredictionResult
{
    public double Value { get; set; }
    public double Confidence { get; set; }
    public TimeSpan TimeToImpact { get; set; }
}

public class PredictionThresholds
{
    public double WarningThreshold { get; set; }
    public double CriticalThreshold { get; set; }
    public double MinConfidence { get; set; }
}

public class PredictedAlert
{
    public string Type { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }
    public double Confidence { get; set; }
    public TimeSpan TimeToImpact { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class CircuitBreakerStatus
{
    public string Status { get; set; }
    public int FailureCount { get; set; }
    public DateTime LastStateChange { get; set; }
}

public class CircuitBreakerEvent
{
    public string EventType { get; set; }
    public DateTime Timestamp { get; set; }
    public string Details { get; set; }
}

public class CircuitBreakerState
{
    public string Status { get; set; }
    public DateTime LastStateChange { get; set; }
    public int FailureCount { get; set; }
    public DateTime NextRetryTime { get; set; }
    public List<CircuitBreakerEvent> RecentEvents { get; set; } = new();
}

public class EncryptionStatus
{
    public bool IsEnabled { get; set; }
    public string CurrentKeyId { get; set; }
    public DateTime KeyCreatedAt { get; set; }
    public DateTime NextRotationDue { get; set; }
    public int KeysInRotation { get; set; }
    public bool AutoRotationEnabled { get; set; }
}

public class MaintenanceLogEntry
{
    public string Operation { get; set; }
    public string Status { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class PolicySwitchEvent
{
    public string FromPolicy { get; set; }
    public string ToPolicy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, double> PerformanceImpact { get; set; } = new();
}

public class PreWarmingMonitoringMetrics
{
    public int BatchSize { get; set; }
    public PreWarmPriority Priority { get; set; }
    public DateTime Timestamp { get; set; }
    public PreWarmingMetrics Metrics { get; set; }
}

public class TrendAnalysis
{
    public string Trend { get; set; }
    public double CurrentValue { get; set; }
    public double ProjectedValue { get; set; }
    public DateTime ProjectionTime { get; set; }
    public double ChangePercent { get; set; }
}

public class CacheAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class CacheHealthStatus
{
    public bool IsHealthy { get; set; } = true;
    public double HitRatio { get; set; }
    public double MemoryUsageMB { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int ActiveAlerts { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
    public Dictionary<string, PredictionResult> Predictions { get; set; } = new();
}

public class AutomaticMitigationService
{
    public Task ApplyMitigations()
    {
        // Mock implementation
        return Task.CompletedTask;
    }
}

public class CacheMonitoringService : ICacheMonitoringService
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly List<CacheAlert> _activeAlerts;
        private MonitoringThresholds _thresholds;
        private readonly Dictionary<string, List<PolicySwitchEvent>> _policySwitchHistory;
        private readonly Dictionary<string, Dictionary<string, double>> _policyPerformanceHistory;
        private readonly List<PreWarmingMonitoringMetrics> _preWarmingMetrics;
        private readonly MLPredictionService _predictionService;
        private readonly AutomaticMitigationService _mitigationService;
        private readonly System.Timers.Timer _mitigationTimer;
        private readonly Dictionary<string, PredictionThresholds> _predictionThresholds;

        public CacheMonitoringService(
            ICacheProvider cacheProvider,
            MLPredictionService predictionService,
            AutomaticMitigationService mitigationService)
        {
            _cacheProvider = cacheProvider;
            _activeAlerts = new List<CacheAlert>();
            _thresholds = new MonitoringThresholds();
            _predictionThresholds = new Dictionary<string, PredictionThresholds>
            {
                ["CacheHitRate"] = new PredictionThresholds 
                { 
                    WarningThreshold = 0.75,
                    CriticalThreshold = 0.6,
                    MinConfidence = 0.7
                },
                ["MemoryUsage"] = new PredictionThresholds 
                { 
                    WarningThreshold = 85,
                    CriticalThreshold = 95,
                    MinConfidence = 0.8
                },
                ["AverageResponseTime"] = new PredictionThresholds 
                { 
                    WarningThreshold = 150,
                    CriticalThreshold = 200,
                    MinConfidence = 0.75
                }
            };
            _policySwitchHistory = new Dictionary<string, List<PolicySwitchEvent>>();
            _policyPerformanceHistory = new Dictionary<string, Dictionary<string, double>>();
            _preWarmingMetrics = new List<PreWarmingMonitoringMetrics>();
            _predictionService = predictionService;
            _mitigationService = mitigationService;

            // Setup automatic mitigation timer
            _mitigationTimer = new System.Timers.Timer(30000); // Check every 30 seconds
            _mitigationTimer.Elapsed += async (sender, e) => await CheckAndApplyMitigations();
            _mitigationTimer.Start();
        }

        private async Task CheckAndApplyMitigations()
        {
            try
            {
                var predictions = await _predictionService.PredictPerformanceMetrics();
                if (ShouldApplyMitigations(predictions))
                {
                    await _mitigationService.ApplyMitigations();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't stop monitoring
                await CreateAlertAsync(
                    "MitigationError",
                    $"Error during automatic mitigation: {ex.Message}",
                    "Warning");
            }
        }

        private bool ShouldApplyMitigations(Dictionary<string, PredictionResult> predictions)
        {
            foreach (var (metric, prediction) in predictions)
            {
                if (prediction.Confidence < 0.7) continue; // Only act on high-confidence predictions

                switch (metric.ToLower())
                {
                    case "cachehitrate" when prediction.Value < _thresholds.MinHitRatio:
                    case "memoryusage" when prediction.Value > _thresholds.MaxMemoryUsageMB * 0.9:
                    case "averageresponsetime" when prediction.Value > _thresholds.MaxResponseTimeMs * 0.9:
                        return true;
                }
            }
            return false;
        }

        public override void Dispose()
        {
            _mitigationTimer?.Dispose();
            base.Dispose();
        }

        public async Task<Dictionary<string, PredictionResult>> GetPerformancePredictionsAsync()
        {
            return await _predictionService.PredictPerformanceMetrics();
        }

        public async Task<Dictionary<string, PredictionThresholds>> GetPredictionThresholdsAsync()
        {
            return _predictionThresholds;
        }

        public async Task UpdatePredictionThresholdsAsync(Dictionary<string, PredictionThresholds> newThresholds)
        {
            foreach (var (metric, threshold) in newThresholds)
            {
                if (_predictionThresholds.ContainsKey(metric))
                {
                    _predictionThresholds[metric] = threshold;
                }
            }
        }

        public async Task<List<PredictedAlert>> GetPredictedAlertsAsync()
        {
            var regularAlerts = await _predictionService.GetPredictedAlerts();
            var precursorPatterns = _predictionService.GetActivePrecursorPatterns();
            var predictions = await _predictionService.PredictPerformanceMetrics();

            // Add alerts based on prediction thresholds
            foreach (var (metric, prediction) in predictions)
            {
                if (_predictionThresholds.TryGetValue(metric, out var thresholds) &&
                    prediction.Confidence >= thresholds.MinConfidence)
                {
                    if (prediction.Value <= thresholds.CriticalThreshold)
                    {
                        regularAlerts.Add(new PredictedAlert
                        {
                            Type = "ThresholdPrediction",
                            Message = $"Predicted {metric} will reach critical level: {prediction.Value:F2}",
                            Severity = "Critical",
                            Confidence = prediction.Confidence,
                            TimeToImpact = prediction.TimeToImpact,
                            Metadata = new Dictionary<string, object>
                            {
                                ["metric"] = metric,
                                ["predictedValue"] = prediction.Value,
                                ["threshold"] = thresholds.CriticalThreshold
                            }
                        });
                    }
                    else if (prediction.Value <= thresholds.WarningThreshold)
                    {
                        regularAlerts.Add(new PredictedAlert
                        {
                            Type = "ThresholdPrediction",
                            Message = $"Predicted {metric} will reach warning level: {prediction.Value:F2}",
                            Severity = "Warning",
                            Confidence = prediction.Confidence,
                            TimeToImpact = prediction.TimeToImpact,
                            Metadata = new Dictionary<string, object>
                            {
                                ["metric"] = metric,
                                ["predictedValue"] = prediction.Value,
                                ["threshold"] = thresholds.WarningThreshold
                            }
                        });
                    }
                }
            }

            // Add precursor pattern alerts
            foreach (var pattern in precursorPatterns)
            {
                regularAlerts.Add(new PredictedAlert
                {
                    Type = "PrecursorPattern",
                    Message = $"Detected pattern that typically precedes {pattern.TargetIssue} " +
                             $"(Lead time: {pattern.LeadTime.TotalMinutes:F1} minutes)",
                    Severity = pattern.Confidence > 0.9 ? "Warning" : "Info",
                    Metadata = new Dictionary<string, object>
                    {
                        ["targetIssue"] = pattern.TargetIssue,
                        ["leadTime"] = pattern.LeadTime,
                        ["confidence"] = pattern.Confidence,
                        ["precursorMetrics"] = pattern.Sequences.Select(s => s.MetricName).ToList()
                    }
                };
            }

            return regularAlerts.OrderByDescending(a => a.Severity).ToList();
        }

        public async Task<Dictionary<string, TrendAnalysis>> GetMetricTrendsAsync()
        {
            return await _predictionService.AnalyzeTrends();
        }

        public async Task<CircuitBreakerStatus> GetCircuitBreakerStatusAsync()
        {
            // Implementation to get circuit breaker status
            throw new NotImplementedException();
        }

        public async Task<CacheHealthStatus> GetHealthStatusAsync()
        {
            var hitRatio = await GetCurrentHitRatioAsync();
            var memoryUsage = await GetMemoryUsageAsync();
            var metrics = await GetPerformanceMetricsAsync();
            var predictions = await _predictionService.PredictPerformanceMetrics();

            return new CacheHealthStatus
            {
                IsHealthy = hitRatio >= _thresholds.MinHitRatio && memoryUsage <= _thresholds.MaxMemoryUsageMB,
                HitRatio = hitRatio,
                MemoryUsageMB = memoryUsage,
                AverageResponseTimeMs = metrics.GetValueOrDefault("AverageResponseTime", 0),
                ActiveAlerts = _activeAlerts.Count,
                LastUpdate = DateTime.UtcNow,
                PerformanceMetrics = metrics,
                Predictions = predictions
            };
        }

        public async Task<List<CacheAlert>> GetActiveAlertsAsync()
        {
            return _activeAlerts;
        }

        public async Task<bool> IsHealthyAsync()
        {
            var health = await GetHealthStatusAsync();
            return health.IsHealthy;
        }

        public async Task RecordPreWarmingMetrics(
            int batchSize,
            PreWarmPriority priority,
            DateTime timestamp,
            PreWarmingMetrics metrics)
        {
            _preWarmingMetrics.Add(new PreWarmingMonitoringMetrics
            {
                BatchSize = batchSize,
                Priority = priority,
                Timestamp = timestamp,
                Metrics = metrics
            });

            // If we have too many metrics, remove old ones
            if (_preWarmingMetrics.Count > 1000)
            {
                _preWarmingMetrics.RemoveRange(0, _preWarmingMetrics.Count - 1000);
            }

            // Create alerts for significant changes
            if (metrics.SuccessRate < 0.5)
            {
                await CreateAlertAsync(
                    "PreWarming",
                    $"Low pre-warming success rate ({metrics.SuccessRate:P2}) for priority {priority}",
                    "Warning",
                    new Dictionary<string, object>
                    {
                        ["success_rate"] = metrics.SuccessRate,
                        ["priority"] = priority,
                        ["batch_size"] = batchSize
                    });
            }

            if (metrics.CacheHitImprovementPercent < 0)
            {
                await CreateAlertAsync(
                    "PreWarming",
                    $"Negative cache hit improvement ({metrics.CacheHitImprovementPercent:F2}%) detected",
                    "Warning",
                    new Dictionary<string, object>
                    {
                        ["improvement"] = metrics.CacheHitImprovementPercent,
                        ["priority"] = priority
                    });
            }
        }

        public async Task<PreWarmingMetrics> GetAggregatePreWarmingMetrics(TimeSpan period)
        {
            var cutoff = DateTime.UtcNow - period;
            var relevantMetrics = _preWarmingMetrics
                .Where(m => m.Timestamp >= cutoff)
                .Select(m => m.Metrics)
                .ToList();

            if (!relevantMetrics.Any())
            {
                return new PreWarmingMetrics();
            }

            return new PreWarmingMetrics
            {
                TotalAttempts = relevantMetrics.Sum(m => m.TotalAttempts),
                SuccessfulPreWarms = relevantMetrics.Sum(m => m.SuccessfulPreWarms),
                AverageResourceUsage = relevantMetrics.Average(m => m.AverageResourceUsage),
                CacheHitImprovementPercent = relevantMetrics.Average(m => m.CacheHitImprovementPercent),
                AverageResponseTimeImprovement = relevantMetrics.Average(m => m.AverageResponseTimeImprovement),
                LastUpdate = relevantMetrics.Max(m => m.LastUpdate)
            };
        }

        public async Task<Dictionary<string, double>> GetPerformanceMetricsAsync()
        {
            var metrics = new Dictionary<string, double>();
            
            // Get basic metrics
            metrics["AverageResponseTime"] = (await GetQueryPerformanceAsync())
                .Values
                .Average(p => p.AverageResponseTime);
            
            metrics["CacheHitRate"] = await GetCurrentHitRatioAsync();
            metrics["MemoryUsage"] = await GetMemoryUsageAsync();
            
            // Add pre-warming metrics
            var preWarmMetrics = await GetAggregatePreWarmingMetrics(TimeSpan.FromHours(24));
            metrics["PreWarmSuccessRate"] = preWarmMetrics.SuccessRate;
            metrics["PreWarmResourceUsage"] = preWarmMetrics.AverageResourceUsage;
            metrics["PreWarmCacheHitImprovement"] = preWarmMetrics.CacheHitImprovementPercent;
            metrics["PreWarmResponseTimeImprovement"] = preWarmMetrics.AverageResponseTimeImprovement;
            
            // Get adaptive policy metrics
            if (_cacheProvider is FileBasedCacheProvider provider)
            {
                var policy = provider.EvictionPolicy as AdaptiveCacheEvictionPolicy;
                if (policy != null)
                {
                    var policyMetrics = policy.GetPolicyMetrics();
                    foreach (var (key, value) in policyMetrics)
                    {
                        metrics[$"adaptive_policy_{key}"] = value;
                    }
                }
            }
            
            // Calculate trend metrics
            metrics["policy_switch_frequency"] = CalculatePolicySwitchFrequency();
            metrics["performance_improvement"] = CalculatePerformanceImprovement();
            
            return metrics;
        }

        public async Task<double> GetCurrentHitRatioAsync()
        {
            // Implementation to calculate hit ratio
            return 0.8;
        }

        public async Task<Dictionary<string, CachePerformanceMetrics>> GetQueryPerformanceAsync()
        {
            // Implementation to get query performance metrics
            throw new NotImplementedException();
        }

        public async Task<double> GetMemoryUsageAsync()
        {
            // Implementation to get memory usage
            return 500;
        }

        public async Task<long> GetStorageUsageAsync()
        {
            // Implementation to get storage usage
            return 1000000;
        }

        public async Task<int> GetCachedItemCountAsync()
        {
            // Implementation to get cached item count
            return 1000;
        }

        public async Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(TimeSpan period)
        {
            // Implementation to get analytics history
            throw new NotImplementedException();
        }

        public async Task<List<string>> GetOptimizationRecommendationsAsync()
        {
            var recommendations = new List<string>();
            var metrics = await GetPerformanceMetricsAsync();
            var switchHistory = GetRecentPolicySwitches();
            
            // Analyze policy switching patterns
            if (switchHistory.Count > 10)
            {
                var switchFrequency = switchHistory.Count / 
                    (DateTime.UtcNow - switchHistory.Min(s => s.Timestamp)).TotalHours;
                
                if (switchFrequency > 2)
                {
                    recommendations.Add(
                        "High policy switch frequency detected. Consider increasing the evaluation interval " +
                        "or adjusting sensitivity thresholds.");
                }
            }
            
            // Analyze performance metrics
            if (metrics.TryGetValue("CacheHitRate", out var hitRate) && hitRate < 0.7)
            {
                recommendations.Add(
                    "Low cache hit rate detected. Consider analyzing access patterns and adjusting " +
                    "cache size or eviction policy preferences.");
            }
            
            if (metrics.TryGetValue("MemoryUsage", out var memoryUsage) && 
                memoryUsage > _thresholds.MaxMemoryUsageMB * 0.9)
            {
                recommendations.Add(
                    "Memory usage approaching threshold. Consider increasing cache size or " +
                    "adjusting eviction policy to be more aggressive.");
            }
            
            // Analyze policy performance
            foreach (var (policy, performance) in _policyPerformanceHistory)
            {
                if (performance.Values.Average() < 0.6)
                {
                    recommendations.Add(
                        $"Policy '{policy}' showing consistently poor performance. Consider adjusting " +
                        "its parameters or reducing its preference weight.");
                }
            }
            
            return recommendations;
        }

        public async Task<string> GeneratePerformanceReportAsync(string format)
        {
            // Implementation to generate performance report
            throw new NotImplementedException();
        }

        public async Task<CacheAlert> CreateAlertAsync(string type, string message, string severity, Dictionary<string, object>? metadata = null)
        {
            var alert = new CacheAlert
            {
                Type = type,
                Message = message,
                Severity = severity,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            _activeAlerts.Add(alert);
            return alert;
        }

        public async Task<bool> ResolveAlertAsync(string alertId)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null)
            {
                _activeAlerts.Remove(alert);
                return true;
            }
            return false;
        }

        public async Task<List<CacheAlert>> GetAlertHistoryAsync(DateTime since)
        {
            // Implementation to get alert history
            throw new NotImplementedException();
        }

        public async Task<bool> UpdateMonitoringThresholdsAsync(MonitoringThresholds thresholds)
        {
            // Validate thresholds before applying
            try {
                thresholds.Validate();
                _thresholds = thresholds;
                return true;
            }
            catch (Exception) {
                return false;
            }
        }

        public async Task<MonitoringThresholds> GetCurrentThresholdsAsync()
        {
            return _thresholds;
        }

        public async Task<bool> TriggerMaintenanceAsync()
        {
            // Implementation to trigger maintenance
            throw new NotImplementedException();
        }

        public async Task<bool> OptimizeCacheAsync()
        {
            // Implementation to optimize cache
            throw new NotImplementedException();
        }

        public async Task<CircuitBreakerState> GetCircuitBreakerStateAsync()
        {
            var circuitBreaker = (_cacheProvider as FileBasedCacheProvider)?.CircuitBreaker;
            if (circuitBreaker == null)
            {
                throw new InvalidOperationException("Circuit breaker not available");
            }

            return new CircuitBreakerState
            {
                Status = circuitBreaker.CurrentState.ToString(),
                LastStateChange = circuitBreaker.LastStateChange,
                FailureCount = circuitBreaker.FailureCount,
                NextRetryTime = circuitBreaker.NextRetryTime,
                RecentEvents = await GetCircuitBreakerHistoryAsync(DateTime.UtcNow.AddHours(-24))
            };
        }

        public async Task<List<CircuitBreakerEvent>> GetCircuitBreakerHistoryAsync(DateTime since)
        {
            // Implementation to get circuit breaker history
            return new List<CircuitBreakerEvent>();
        }

        public async Task<bool> IsCircuitBreakerTrippedAsync()
        {
            var state = await GetCircuitBreakerStateAsync();
            return state.Status == "Open";
        }

        public async Task<EncryptionStatus> GetEncryptionStatusAsync()
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            if (provider?.EncryptionProvider == null)
            {
                throw new InvalidOperationException("Encryption provider not available");
            }

            return new EncryptionStatus
            {
                IsEnabled = provider.EncryptionProvider.IsEnabled,
                CurrentKeyId = provider.EncryptionProvider.CurrentKeyId,
                KeyCreatedAt = provider.EncryptionProvider.CurrentKeyCreatedAt,
                NextRotationDue = await GetNextKeyRotationTimeAsync(),
                KeysInRotation = (await GetActiveEncryptionKeysAsync()).Count,
                AutoRotationEnabled = provider.EncryptionProvider.AutoRotationEnabled
            };
        }

        public async Task<DateTime> GetNextKeyRotationTimeAsync()
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            return provider?.EncryptionProvider?.NextScheduledRotation ?? DateTime.MaxValue;
        }

        public async Task<Dictionary<string, DateTime>> GetKeyRotationScheduleAsync()
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            return provider?.EncryptionProvider?.GetKeyRotationSchedule() ?? new Dictionary<string, DateTime>();
        }

        public async Task<List<string>> GetActiveEncryptionKeysAsync()
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            return provider?.EncryptionProvider?.GetActiveKeyIds().ToList() ?? new List<string>();
        }

        public async Task<List<MaintenanceLogEntry>> GetMaintenanceLogsAsync(DateTime since)
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            return provider?.MaintenanceLogs.Where(log => log.Timestamp >= since).ToList() 
                   ?? new List<MaintenanceLogEntry>();
        }

        public async Task<Dictionary<string, int>> GetEvictionStatisticsAsync()
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            return provider?.GetEvictionStats() ?? new Dictionary<string, int>();
        }

        public async Task<List<CacheEvictionEvent>> GetRecentEvictionsAsync(int count)
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            return provider?.GetRecentEvictions(count).ToList() ?? new List<CacheEvictionEvent>();
        }

        private double CalculatePolicySwitchFrequency()
        {
            var recentSwitches = GetRecentPolicySwitches();
            if (recentSwitches.Count < 2) return 0;

            var timeSpan = recentSwitches.Max(s => s.Timestamp) - recentSwitches.Min(s => s.Timestamp);
            return recentSwitches.Count / timeSpan.TotalHours;
        }

        private double CalculatePerformanceImprovement()
        {
            var recentSwitches = GetRecentPolicySwitches();
            if (recentSwitches.Count == 0) return 0;

            return recentSwitches.Average(s => 
                s.PerformanceImpact.GetValueOrDefault("score_improvement", 0));
        }

        private List<PolicySwitchEvent> GetRecentPolicySwitches(TimeSpan? period = null)
        {
            period ??= TimeSpan.FromDays(1);
            var cutoff = DateTime.UtcNow - period.Value;

            return _policySwitchHistory.Values
                .SelectMany(x => x)
                .Where(x => x.Timestamp >= cutoff)
                .OrderByDescending(x => x.Timestamp)
                .ToList();
        }

        public async Task RecordPolicySwitchAsync(PolicySwitchEvent switchEvent)
        {
            var key = $"{switchEvent.FromPolicy}->{switchEvent.ToPolicy}";
            if (!_policySwitchHistory.ContainsKey(key))
            {
                _policySwitchHistory[key] = new List<PolicySwitchEvent>();
            }
            _policySwitchHistory[key].Add(switchEvent);

            // Update performance history
            if (!_policyPerformanceHistory.ContainsKey(switchEvent.ToPolicy))
            {
                _policyPerformanceHistory[switchEvent.ToPolicy] = new Dictionary<string, double>();
            }
            
            foreach (var (metric, value) in switchEvent.PerformanceImpact)
            {
                _policyPerformanceHistory[switchEvent.ToPolicy][metric] = value;
            }

            // Create alert if significant performance change
            var performanceChange = switchEvent.PerformanceImpact
                .GetValueOrDefault("score_improvement", 0);
            
            if (Math.Abs(performanceChange) > 0.1)
            {
                var impact = performanceChange > 0 ? "improvement" : "degradation";
                await CreateAlertAsync(
                    "PolicySwitch",
                    $"Cache policy switched from {switchEvent.FromPolicy} to {switchEvent.ToPolicy} " +
                    $"with {Math.Abs(performanceChange):P2} performance {impact}",
                    performanceChange > 0 ? "Info" : "Warning",
                    new Dictionary<string, object>
                    {
                        ["from_policy"] = switchEvent.FromPolicy,
                        ["to_policy"] = switchEvent.ToPolicy,
                        ["performance_change"] = performanceChange,
                        ["timestamp"] = switchEvent.Timestamp
                    });
            }
        }

        public async Task LogMaintenanceEventAsync(string operation, string status, TimeSpan duration, Dictionary<string, object>? metadata = null)
        {
            var provider = _cacheProvider as FileBasedCacheProvider;
            if (provider == null) return;

            var logEntry = new MaintenanceLogEntry
            {
                Operation = operation,
                Status = status,
                Duration = duration,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            provider.MaintenanceLogs.Add(logEntry);

            // Notify subscribers of the new maintenance event
            if (status == "Warning" || status == "Error")
            {
                await CreateAlertAsync("Maintenance", $"Operation '{operation}' completed with status: {status}", "Warning", metadata);
            }
        }
    }
}