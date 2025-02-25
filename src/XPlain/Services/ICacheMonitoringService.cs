using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheMonitoringService : IDisposable
    {
        Task<CacheHealthStatus> GetHealthStatusAsync();
        Task<Dictionary<string, double>> GetPerformanceMetricsAsync();
        Task<List<CacheAlert>> GetActiveAlertsAsync();
        Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(TimeSpan period);
        Task<List<string>> GetOptimizationRecommendationsAsync();
        Task<MonitoringThresholds> GetCurrentThresholdsAsync();
        Task<bool> UpdateMonitoringThresholdsAsync(MonitoringThresholds thresholds);
        Task<string> GeneratePerformanceReportAsync(string format);
        Task<bool> CreateAlertAsync(string type, string message, string severity, Dictionary<string, object>? metadata = null);
        Task<bool> ResolveAlertAsync(string alertId);
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

    public class CacheAnalytics
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public CacheStats Stats { get; set; }
        public double MemoryUsageMB { get; set; }
        public double CpuUsagePercent { get; set; }
        public Dictionary<string, object> CustomMetrics { get; set; } = new();
    }
}