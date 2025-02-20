using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class CacheMonitoringService : ICacheMonitoringService, IDisposable
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly CacheSettings _settings;
        private readonly ConcurrentDictionary<string, CacheAlert> _activeAlerts;
        private readonly ConcurrentQueue<CacheAlert> _alertHistory;
        private MonitoringThresholds _thresholds;
        private readonly Timer _healthCheckTimer;
        private readonly Timer _maintenanceTimer;
        private readonly object _monitoringLock = new();
        private CacheHealthStatus _lastHealthStatus;

        public CacheMonitoringService(
            ICacheProvider cacheProvider,
            IOptions<CacheSettings> settings)
        {
            _cacheProvider = cacheProvider;
            _settings = settings.Value;
            _activeAlerts = new ConcurrentDictionary<string, CacheAlert>();
            _alertHistory = new ConcurrentQueue<CacheAlert>();
            _thresholds = new MonitoringThresholds();
            _lastHealthStatus = new CacheHealthStatus
            {
                IsHealthy = true,
                LastUpdate = DateTime.UtcNow
            };

            // Initialize health check timer (every 1 minute)
            _healthCheckTimer = new Timer(
                async _ => await PerformHealthCheckAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1)
            );

            // Initialize maintenance timer (every 6 hours)
            _maintenanceTimer = new Timer(
                async _ => await TriggerMaintenanceAsync(),
                null,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(6)
            );
        }

        public async Task<CacheHealthStatus> GetHealthStatusAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            var memoryUsage = stats.StorageUsageBytes / (1024.0 * 1024.0);
            var avgResponseTime = stats.AverageResponseTimes.Values.DefaultIfEmpty(0).Average();

            return new CacheHealthStatus
            {
                IsHealthy = await IsHealthyAsync(),
                HitRatio = stats.HitRatio,
                MemoryUsageMB = memoryUsage,
                AverageResponseTimeMs = avgResponseTime,
                ActiveAlerts = _activeAlerts.Count,
                LastUpdate = DateTime.UtcNow,
                PerformanceMetrics = await GetPerformanceMetricsAsync()
            };
        }

        public Task<List<CacheAlert>> GetActiveAlertsAsync()
        {
            return Task.FromResult(_activeAlerts.Values.ToList());
        }

        public async Task<bool> IsHealthyAsync()
        {
            var status = await GetHealthStatusAsync();
            return status.HitRatio >= _thresholds.MinHitRatio &&
                   status.MemoryUsageMB <= _thresholds.MaxMemoryUsageMB &&
                   status.AverageResponseTimeMs <= _thresholds.MaxResponseTimeMs &&
                   _activeAlerts.Count <= _thresholds.MaxConcurrentAlerts;
        }

        public async Task<Dictionary<string, double>> GetPerformanceMetricsAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            return new Dictionary<string, double>
            {
                ["HitRatio"] = stats.HitRatio,
                ["AverageResponseTime"] = stats.AverageResponseTimes.Values.DefaultIfEmpty(0).Average(),
                ["CacheSize"] = stats.StorageUsageBytes / (1024.0 * 1024.0),
                ["ItemCount"] = stats.CachedItemCount,
                ["InvalidationRate"] = stats.InvalidationHistory
                    .Where(h => h.Time >= DateTime.UtcNow.AddHours(-1))
                    .Sum(h => h.ItemsInvalidated) / 60.0 // per minute
            };
        }

        public Task<double> GetCurrentHitRatioAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            return Task.FromResult(stats.HitRatio);
        }

        public Task<Dictionary<string, CachePerformanceMetrics>> GetQueryPerformanceAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            return Task.FromResult(stats.PerformanceByQueryType);
        }

        public Task<double> GetMemoryUsageAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            return Task.FromResult(stats.StorageUsageBytes / (1024.0 * 1024.0));
        }

        public Task<long> GetStorageUsageAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            return Task.FromResult(stats.StorageUsageBytes);
        }

        public Task<int> GetCachedItemCountAsync()
        {
            var stats = _cacheProvider.GetCacheStats();
            return Task.FromResult(stats.CachedItemCount);
        }

        public async Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(TimeSpan period)
        {
            return await _cacheProvider.GetAnalyticsHistoryAsync(DateTime.UtcNow - period);
        }

        public async Task<List<string>> GetOptimizationRecommendationsAsync()
        {
            return await _cacheProvider.GetCacheWarmingRecommendationsAsync();
        }

        public async Task<string> GeneratePerformanceReportAsync(string format)
        {
            return await _cacheProvider.GeneratePerformanceChartAsync(
                Enum.Parse<OutputFormat>(format, true));
        }

        public async Task<CacheAlert> CreateAlertAsync(
            string type,
            string message,
            string severity,
            Dictionary<string, object>? metadata = null)
        {
            var alert = new CacheAlert
            {
                Type = type,
                Message = message,
                Severity = severity,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            if (_activeAlerts.TryAdd(alert.Id, alert))
            {
                _alertHistory.Enqueue(alert);
                while (_alertHistory.Count > 1000) // Keep last 1000 alerts
                {
                    _alertHistory.TryDequeue(out _);
                }
            }

            return alert;
        }

        public Task<bool> ResolveAlertAsync(string alertId)
        {
            return Task.FromResult(_activeAlerts.TryRemove(alertId, out _));
        }

        public Task<List<CacheAlert>> GetAlertHistoryAsync(DateTime since)
        {
            return Task.FromResult(
                _alertHistory
                    .Where(a => a.Timestamp >= since)
                    .OrderByDescending(a => a.Timestamp)
                    .ToList()
            );
        }

        public Task UpdateMonitoringThresholdsAsync(MonitoringThresholds thresholds)
        {
            _thresholds = thresholds;
            return Task.CompletedTask;
        }

        public Task<MonitoringThresholds> GetCurrentThresholdsAsync()
        {
            return Task.FromResult(_thresholds);
        }

        public async Task<bool> TriggerMaintenanceAsync()
        {
            try
            {
                var beforeStats = await GetPerformanceMetricsAsync();
                
                // Trigger maintenance routine
                await Task.Delay(TimeSpan.FromSeconds(1)); // Simulate maintenance work
                
                var afterStats = await GetPerformanceMetricsAsync();
                
                // Check if maintenance improved performance
                var hitRatioImprovement = afterStats["HitRatio"] - beforeStats["HitRatio"];
                var responseTimeImprovement = beforeStats["AverageResponseTime"] - afterStats["AverageResponseTime"];
                
                if (hitRatioImprovement > 0.1 || responseTimeImprovement > 50)
                {
                    await CreateAlertAsync(
                        "Maintenance",
                        "Cache maintenance completed successfully with significant improvements",
                        "Info",
                        new Dictionary<string, object>
                        {
                            ["HitRatioImprovement"] = hitRatioImprovement,
                            ["ResponseTimeImprovement"] = responseTimeImprovement
                        });
                }

                return true;
            }
            catch (Exception ex)
            {
                await CreateAlertAsync(
                    "MaintenanceError",
                    $"Cache maintenance failed: {ex.Message}",
                    "Error");
                return false;
            }
        }

        public async Task<bool> OptimizeCacheAsync()
        {
            try
            {
                var recommendations = await GetOptimizationRecommendationsAsync();
                if (recommendations.Any())
                {
                    await CreateAlertAsync(
                        "Optimization",
                        "Cache optimization recommendations available",
                        "Info",
                        new Dictionary<string, object>
                        {
                            ["Recommendations"] = recommendations
                        });
                }

                return true;
            }
            catch (Exception ex)
            {
                await CreateAlertAsync(
                    "OptimizationError",
                    $"Cache optimization failed: {ex.Message}",
                    "Error");
                return false;
            }
        }

        private async Task PerformHealthCheckAsync()
        {
            try
            {
                var currentStatus = await GetHealthStatusAsync();
                var previousStatus = _lastHealthStatus;

                // Check for significant changes
                if (Math.Abs(currentStatus.HitRatio - previousStatus.HitRatio) > 0.1)
                {
                    await CreateAlertAsync(
                        "HitRatioChange",
                        $"Cache hit ratio changed significantly: {previousStatus.HitRatio:P} -> {currentStatus.HitRatio:P}",
                        currentStatus.HitRatio < previousStatus.HitRatio ? "Warning" : "Info");
                }

                if (currentStatus.MemoryUsageMB > _thresholds.MaxMemoryUsageMB)
                {
                    await CreateAlertAsync(
                        "HighMemoryUsage",
                        $"Cache memory usage exceeds threshold: {currentStatus.MemoryUsageMB:F2}MB",
                        "Warning");
                }

                if (currentStatus.AverageResponseTimeMs > _thresholds.MaxResponseTimeMs)
                {
                    await CreateAlertAsync(
                        "HighLatency",
                        $"Cache response time exceeds threshold: {currentStatus.AverageResponseTimeMs:F2}ms",
                        "Warning");
                }

                _lastHealthStatus = currentStatus;
            }
            catch (Exception ex)
            {
                await CreateAlertAsync(
                    "HealthCheckError",
                    $"Health check failed: {ex.Message}",
                    "Error");
            }
        }

        public void Dispose()
        {
            _healthCheckTimer?.Dispose();
            _maintenanceTimer?.Dispose();
        }
    }
}