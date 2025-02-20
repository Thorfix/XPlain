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

    public record MonitoringThresholds
    {
        public double MinHitRatio { get; init; } = 0.7;
        public double MaxMemoryUsageMB { get; init; } = 1000;
        public double MaxResponseTimeMs { get; init; } = 500;
        public double PerformanceDegradationThreshold { get; init; } = 20;
        public int MaxConcurrentAlerts { get; init; } = 10;
    }

    public interface ICacheMonitoringService
    {
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
    }
}