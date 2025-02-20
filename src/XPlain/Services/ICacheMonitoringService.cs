using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace XPlain.Services
{
    public record CacheAlert
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string Type { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Severity { get; init; } = "Info";
        public Dictionary<string, object> Metadata { get; init; } = new();
    }

    public record CacheHealthStatus
    {
        public bool IsHealthy { get; init; }
        public double HitRatio { get; init; }
        public double MemoryUsageMB { get; init; }
        public double AverageResponseTimeMs { get; init; }
        public int ActiveAlerts { get; init; }
        public DateTime LastUpdate { get; init; }
        public Dictionary<string, double> PerformanceMetrics { get; init; } = new();
    }

    public record EncryptionStatus
    {
        public bool IsEnabled { get; init; }
        public string CurrentKeyId { get; init; } = string.Empty;
        public DateTime KeyCreatedAt { get; init; }
        public DateTime NextRotationDue { get; init; }
        public int KeysInRotation { get; init; }
        public bool AutoRotationEnabled { get; init; }
    }

    public record MaintenanceLogEntry
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string Operation { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public TimeSpan Duration { get; init; }
        public Dictionary<string, object> Metadata { get; init; } = new();
    }

    public record CircuitBreakerState
    {
        public string Status { get; init; } = string.Empty;
        public DateTime LastStateChange { get; init; }
        public int FailureCount { get; init; }
        public DateTime? NextRetryTime { get; init; }
        public List<CircuitBreakerEvent> RecentEvents { get; init; } = new();
    }

    public record CircuitBreakerEvent
    {
        public DateTime Timestamp { get; init; }
        public string FromState { get; init; } = string.Empty;
        public string ToState { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    public record MonitoringThresholds
    {
        public double MinHitRatio { get; init; } = 0.7;
        public double MaxMemoryUsageMB { get; init; } = 1000;
        public double MaxResponseTimeMs { get; init; } = 500;
        public double PerformanceDegradationThreshold { get; init; } = 20;
        public int MaxConcurrentAlerts { get; init; } = 10;
        public int MaxFailuresBeforeBreaking { get; init; } = 5;
        public double MaxEvictionRatePerMinute { get; init; } = 1000;
        public int MaxStorageUsagePercent { get; init; } = 90;
    }

    public interface ICacheMonitoringService
    {
        /// <summary>
        /// Gets the current state of the circuit breaker
        /// </summary>
        Task<CircuitBreakerStatus> GetCircuitBreakerStatusAsync();

        // Real-time monitoring
        Task<CacheHealthStatus> GetHealthStatusAsync();
        Task<List<CacheAlert>> GetActiveAlertsAsync();
        Task<bool> IsHealthyAsync();

        // Performance monitoring
        Task<Dictionary<string, double>> GetPerformanceMetricsAsync();
        Task<double> GetCurrentHitRatioAsync();
        Task<Dictionary<string, CachePerformanceMetrics>> GetQueryPerformanceAsync();

        // Resource monitoring
        Task<double> GetMemoryUsageAsync();
        Task<long> GetStorageUsageAsync();
        Task<int> GetCachedItemCountAsync();

        // Analytics
        Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(TimeSpan period);
        Task<List<string>> GetOptimizationRecommendationsAsync();
        Task<string> GeneratePerformanceReportAsync(string format);

        // Alert management
        Task<CacheAlert> CreateAlertAsync(string type, string message, string severity, Dictionary<string, object>? metadata = null);
        Task<bool> ResolveAlertAsync(string alertId);
        Task<List<CacheAlert>> GetAlertHistoryAsync(DateTime since);

        // Configuration
        Task UpdateMonitoringThresholdsAsync(MonitoringThresholds thresholds);
        Task<MonitoringThresholds> GetCurrentThresholdsAsync();

        // Maintenance
        Task<bool> TriggerMaintenanceAsync();
        Task<bool> OptimizeCacheAsync();

        // Circuit Breaker monitoring
        Task<CircuitBreakerState> GetCircuitBreakerStateAsync();
        Task<List<CircuitBreakerEvent>> GetCircuitBreakerHistoryAsync(DateTime since);
        Task<bool> IsCircuitBreakerTrippedAsync();

        // Encryption monitoring
        Task<EncryptionStatus> GetEncryptionStatusAsync();
        Task<DateTime> GetNextKeyRotationTimeAsync();
        Task<Dictionary<string, DateTime>> GetKeyRotationScheduleAsync();
        Task<List<string>> GetActiveEncryptionKeysAsync();

        // Maintenance and operations
        Task<List<MaintenanceLogEntry>> GetMaintenanceLogsAsync(DateTime since);
        Task<Dictionary<string, int>> GetEvictionStatisticsAsync();
        Task<List<CacheEvictionEvent>> GetRecentEvictionsAsync(int count);
        Task LogMaintenanceEventAsync(string operation, string status, TimeSpan duration, Dictionary<string, object>? metadata = null);
    }
}