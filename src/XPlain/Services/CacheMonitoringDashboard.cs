using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class CacheMonitoringDashboard
    {
        private readonly ICacheMonitoringService _monitoringService;

        public CacheMonitoringDashboard(ICacheMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
        }

        public async Task<string> GenerateDashboardAsync(OutputFormat format)
        {
            var healthStatus = await _monitoringService.GetHealthStatusAsync();
            var alerts = await _monitoringService.GetActiveAlertsAsync();
            var metrics = await _monitoringService.GetPerformanceMetricsAsync();
            var recommendations = await _monitoringService.GetOptimizationRecommendationsAsync();

            switch (format)
            {
                case OutputFormat.Markdown:
                    return await GenerateMarkdownDashboardAsync(healthStatus, alerts, metrics, recommendations);
                case OutputFormat.Json:
                    return await GenerateJsonDashboardAsync(healthStatus, alerts, metrics, recommendations);
                default:
                    return await GenerateTextDashboardAsync(healthStatus, alerts, metrics, recommendations);
            }
        }

        private async Task<string> GenerateMarkdownDashboardAsync(
            CacheHealthStatus health,
            List<CacheAlert> alerts,
            Dictionary<string, double> metrics,
            List<string> recommendations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Cache Monitoring Dashboard");
            sb.AppendLine($"*Last Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}*");
            sb.AppendLine();

            // Health Status
            sb.AppendLine("## System Health");
            sb.AppendLine($"Status: {(health.IsHealthy ? "✅ Healthy" : "❌ Issues Detected")}");
            sb.AppendLine($"- Hit Ratio: {health.HitRatio:P2}");
            sb.AppendLine($"- Memory Usage: {health.MemoryUsageMB:F2} MB");
            sb.AppendLine($"- Average Response Time: {health.AverageResponseTimeMs:F2} ms");
            sb.AppendLine($"- Active Alerts: {health.ActiveAlerts}");
            sb.AppendLine();

            // Active Alerts
            sb.AppendLine("## Active Alerts");
            if (alerts.Count == 0)
            {
                sb.AppendLine("*No active alerts*");
            }
            else
            {
                sb.AppendLine("| Severity | Type | Message | Time |");
                sb.AppendLine("|----------|------|---------|------|");
                foreach (var alert in alerts)
                {
                    sb.AppendLine($"| {alert.Severity} | {alert.Type} | {alert.Message} | {alert.Timestamp:HH:mm:ss} |");
                }
            }
            sb.AppendLine();

            // Performance Metrics
            sb.AppendLine("## Performance Metrics");
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|-------|");
            foreach (var (key, value) in metrics)
            {
                sb.AppendLine($"| {key} | {FormatMetricValue(key, value)} |");
            }
            sb.AppendLine();

            // Performance Chart
            sb.AppendLine("## Performance Trends");
            sb.AppendLine("```");
            var chart = await _monitoringService.GeneratePerformanceReportAsync("Text");
            sb.AppendLine(chart);
            sb.AppendLine("```");
            sb.AppendLine();

            // Recommendations
            sb.AppendLine("## Optimization Recommendations");
            if (recommendations.Count == 0)
            {
                sb.AppendLine("*No recommendations at this time*");
            }
            else
            {
                foreach (var rec in recommendations)
                {
                    sb.AppendLine($"- {rec}");
                }
            }

            return sb.ToString();
        }

        private async Task<string> GenerateJsonDashboardAsync(
            CacheHealthStatus health,
            List<CacheAlert> alerts,
            Dictionary<string, double> metrics,
            List<string> recommendations)
        {
            var dashboardData = new
            {
                timestamp = DateTime.UtcNow,
                health = new
                {
                    status = health.IsHealthy ? "healthy" : "issues",
                    hitRatio = health.HitRatio,
                    memoryUsageMB = health.MemoryUsageMB,
                    avgResponseTimeMs = health.AverageResponseTimeMs,
                    activeAlerts = health.ActiveAlerts
                },
                alerts = alerts,
                metrics = metrics,
                recommendations = recommendations,
                trends = await _monitoringService.GeneratePerformanceReportAsync("Json")
            };

            return System.Text.Json.JsonSerializer.Serialize(
                dashboardData,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private async Task<string> GenerateTextDashboardAsync(
            CacheHealthStatus health,
            List<CacheAlert> alerts,
            Dictionary<string, double> metrics,
            List<string> recommendations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Cache Monitoring Dashboard");
            sb.AppendLine("=========================");
            sb.AppendLine($"Last Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            sb.AppendLine();

            // Health Status
            sb.AppendLine("System Health");
            sb.AppendLine("------------");
            sb.AppendLine($"Status: {(health.IsHealthy ? "Healthy" : "Issues Detected")}");
            sb.AppendLine($"Hit Ratio: {health.HitRatio:P2}");
            sb.AppendLine($"Memory Usage: {health.MemoryUsageMB:F2} MB");
            sb.AppendLine($"Average Response Time: {health.AverageResponseTimeMs:F2} ms");
            sb.AppendLine($"Active Alerts: {health.ActiveAlerts}");
            sb.AppendLine();

            // Active Alerts
            sb.AppendLine("Active Alerts");
            sb.AppendLine("------------");
            if (alerts.Count == 0)
            {
                sb.AppendLine("No active alerts");
            }
            else
            {
                foreach (var alert in alerts)
                {
                    sb.AppendLine($"[{alert.Severity}] {alert.Type}: {alert.Message}");
                    sb.AppendLine($"Time: {alert.Timestamp:HH:mm:ss}");
                }
            }
            sb.AppendLine();

            // Performance Metrics
            sb.AppendLine("Performance Metrics");
            sb.AppendLine("------------------");
            foreach (var (key, value) in metrics)
            {
                sb.AppendLine($"{key}: {FormatMetricValue(key, value)}");
            }
            sb.AppendLine();

            // Performance Chart
            sb.AppendLine("Performance Trends");
            sb.AppendLine("-----------------");
            var chart = await _monitoringService.GeneratePerformanceReportAsync("Text");
            sb.AppendLine(chart);
            sb.AppendLine();

            // Recommendations
            sb.AppendLine("Optimization Recommendations");
            sb.AppendLine("--------------------------");
            if (recommendations.Count == 0)
            {
                sb.AppendLine("No recommendations at this time");
            }
            else
            {
                foreach (var rec in recommendations)
                {
                    sb.AppendLine($"- {rec}");
                }
            }

            return sb.ToString();
        }

        private string FormatMetricValue(string metricName, double value)
        {
            return metricName.ToLower() switch
            {
                var m when m.Contains("ratio") => $"{value:P2}",
                var m when m.Contains("memory") || m.Contains("size") => $"{value:F2} MB",
                var m when m.Contains("time") || m.Contains("latency") => $"{value:F2} ms",
                var m when m.Contains("count") || m.Contains("items") => $"{value:N0}",
                _ => $"{value:F2}"
            };
        }
    }
}