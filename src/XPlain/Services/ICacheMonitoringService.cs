using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheMonitoringService
    {
        Task<CacheHealthStatus> GetHealthStatusAsync();
        Task<Dictionary<string, object>> GetPerformanceMetricsAsync();
        Task<List<CacheAlert>> GetActiveAlertsAsync();
        Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(TimeSpan timeSpan);
        Task<List<string>> GetOptimizationRecommendationsAsync();
        Task<MonitoringThresholds> GetCurrentThresholdsAsync();
        Task<bool> UpdateMonitoringThresholdsAsync(MonitoringThresholds thresholds);
        Task<string> GeneratePerformanceReportAsync(string format);
    }

    public class CacheHealthStatus
    {
        public string Status { get; set; } = "Healthy";
        public double HealthScore { get; set; } = 100.0;
        public bool IsOperational { get; set; } = true;
        public Dictionary<string, bool> Checks { get; set; } = new();
        public string LastCheckTimestamp { get; set; } = DateTime.UtcNow.ToString("o");
        public List<string> Issues { get; set; } = new();
    }

    public class CacheAlert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string Description { get; set; }
        public CacheAlertSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public enum CacheAlertSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class CacheAnalytics
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public CacheStats Stats { get; set; }
        public double MemoryUsageMB { get; set; }
        public double CpuUsagePercent { get; set; }
        public Dictionary<string, object> CustomMetrics { get; set; } = new();
    }

    // Using the MonitoringThresholds class from the main namespace
}