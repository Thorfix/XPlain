using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheMonitoringService
    {
        Task<CircuitBreakerStatus> GetCircuitBreakerStatusAsync();
        Task<CacheHealthStatus> GetHealthStatusAsync();
        Task<List<CacheAlert>> GetActiveAlertsAsync();
        Task<bool> IsHealthyAsync();
        Task<Dictionary<string, double>> GetPerformanceMetricsAsync();
        Task<double> GetCurrentHitRatioAsync();
        Task<Dictionary<string, CachePerformanceMetrics>> GetQueryPerformanceAsync();
        Task<double> GetMemoryUsageAsync();
        Task<long> GetStorageUsageAsync();
        Task<int> GetCachedItemCountAsync();
        Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(TimeSpan period);
        Task<List<string>> GetOptimizationRecommendationsAsync();
        Task<string> GeneratePerformanceReportAsync(string format);
        Task<CacheAlert> CreateAlertAsync(string type, string message, string severity, Dictionary<string, object>? metadata = null);
        Task<bool> ResolveAlertAsync(string alertId);
        Task<List<CacheAlert>> GetAlertHistoryAsync(DateTime since);
        Task UpdateMonitoringThresholdsAsync(MonitoringThresholds thresholds);
        Task<MonitoringThresholds> GetCurrentThresholdsAsync();
        Task<bool> TriggerMaintenanceAsync();
        Task<bool> OptimizeCacheAsync();
        Task<CircuitBreakerState> GetCircuitBreakerStateAsync();
        Task<List<CircuitBreakerEvent>> GetCircuitBreakerHistoryAsync(DateTime since);
        Task<bool> IsCircuitBreakerTrippedAsync();
        Task<EncryptionStatus> GetEncryptionStatusAsync();
        Task<DateTime> GetNextKeyRotationTimeAsync();
        Task<Dictionary<string, DateTime>> GetKeyRotationScheduleAsync();
        Task<List<string>> GetActiveEncryptionKeysAsync();
        Task<List<MaintenanceLogEntry>> GetMaintenanceLogsAsync(DateTime since);
        Task<Dictionary<string, int>> GetEvictionStatisticsAsync();
        Task<List<CacheEvictionEvent>> GetRecentEvictionsAsync(int count);
        Task RecordPolicySwitchAsync(PolicySwitchEvent switchEvent);
        Task LogMaintenanceEventAsync(string operation, string status, TimeSpan duration, Dictionary<string, object>? metadata = null);
        
        // New ML-related methods
        Task<Dictionary<string, PredictionResult>> GetPerformancePredictionsAsync();
        Task<List<PredictedAlert>> GetPredictedAlertsAsync();
        Task<Dictionary<string, TrendAnalysis>> GetMetricTrendsAsync();
    }
}