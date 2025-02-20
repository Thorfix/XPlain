using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class MetricsRecord
    {
        public DateTime Timestamp { get; set; }
        public double ResponseTime { get; set; }
        public bool IsHit { get; set; }
        public int RequestCount { get; set; }
    }

    public class MetricsCollectionService
    {
        private readonly ILogger<MetricsCollectionService> _logger;
        private readonly TimeSeriesMetricsStore _timeSeriesStore;
        private readonly MetricsSettings _settings;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>> _metricsStore;
        private readonly ConcurrentDictionary<string, long> _queryCounters;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _cleanupLock = new SemaphoreSlim(1, 1);

        public MetricsCollectionService(
            ILogger<MetricsCollectionService> logger,
            TimeSeriesMetricsStore timeSeriesStore,
            IOptions<MetricsSettings> settings)
        {
            _logger = logger;
            _timeSeriesStore = timeSeriesStore;
            _settings = settings.Value;
            _metricsStore = new ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>>();
            _queryCounters = new ConcurrentDictionary<string, long>();
            _cleanupTimer = new Timer(CleanupOldMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task RecordQueryMetrics(string key, double responseTime, bool isHit)
        {
            try
            {
                // Store in memory for immediate access
                var metrics = new MetricsRecord
                {
                    Timestamp = DateTime.UtcNow,
                    ResponseTime = responseTime,
                    IsHit = isHit,
                    RequestCount = 1
                };

                var queue = _metricsStore.GetOrAdd(key, _ => new ConcurrentQueue<MetricsRecord>());
                queue.Enqueue(metrics);

                _queryCounters.AddOrUpdate(key, 1, (_, count) => count + 1);

                // Persist to time series database
                await _timeSeriesStore.StoreQueryMetric(key, responseTime, isHit, metrics.Timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording metrics for key: {Key}", key);
            }
        }

        public async Task<double> GetQueryFrequency(string key, TimeSpan window)
        {
            try
            {
                // Try to get from time series store first
                var frequency = await _timeSeriesStore.GetQueryFrequency(key, window);
                if (frequency > 0) return frequency;

                // Fall back to in-memory data if time series query fails
                var queue = _metricsStore.GetOrAdd(key, _ => new ConcurrentQueue<MetricsRecord>());
                var cutoff = DateTime.UtcNow - window;
                var recentRequests = queue.Where(m => m.Timestamp >= cutoff).Sum(m => m.RequestCount);
                
                return recentRequests / window.TotalHours;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating query frequency for key: {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetAverageResponseTime(string key, TimeSpan window)
        {
            try
            {
                // Try to get from time series store first
                var avgResponseTime = await _timeSeriesStore.GetAverageResponseTime(key, window);
                if (avgResponseTime > 0) return avgResponseTime;

                // Fall back to in-memory data if time series query fails
                var queue = _metricsStore.GetOrAdd(key, _ => new ConcurrentQueue<MetricsRecord>());
                var cutoff = DateTime.UtcNow - window;
                var metrics = queue.Where(m => m.Timestamp >= cutoff).ToList();

                if (!metrics.Any()) return 0;

                return metrics.Average(m => m.ResponseTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating average response time for key: {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetCacheHitRate(string key, TimeSpan window)
        {
            try
            {
                // Try to get from time series store first
                var hitRate = await _timeSeriesStore.GetCacheHitRate(key, window);
                if (hitRate >= 0) return hitRate;

                // Fall back to in-memory data if time series query fails
                var queue = _metricsStore.GetOrAdd(key, _ => new ConcurrentQueue<MetricsRecord>());
                var cutoff = DateTime.UtcNow - window;
                var metrics = queue.Where(m => m.Timestamp >= cutoff).ToList();

                if (!metrics.Any()) return 0;

                var hits = metrics.Count(m => m.IsHit);
                return (double)hits / metrics.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating cache hit rate for key: {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetUserActivityLevel(TimeSpan window)
        {
            try
            {
                // Try to get from time series store first
                var activityLevel = await _timeSeriesStore.GetUserActivityLevel(window);
                if (activityLevel >= 0) return activityLevel;

                // Fall back to in-memory data if time series query fails
                var cutoff = DateTime.UtcNow - window;
                var totalRequests = _metricsStore.Values
                    .SelectMany(q => q.Where(m => m.Timestamp >= cutoff))
                    .Sum(m => m.RequestCount);

                var requestsPerHour = totalRequests / window.TotalHours;
                return Math.Min(1.0, requestsPerHour / 1000.0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating user activity level");
                return 0;
            }
        }

        private async void CleanupOldMetrics(object? state)
        {
            if (!await _cleanupLock.WaitAsync(0)) return;

            try
            {
                // Cleanup time series database
                await _timeSeriesStore.CleanupOldMetrics();

                // Cleanup in-memory cache
                var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1); // Keep only last hour in memory

                foreach (var key in _metricsStore.Keys)
                {
                    if (_metricsStore.TryGetValue(key, out var queue))
                    {
                        var newQueue = new ConcurrentQueue<MetricsRecord>(
                            queue.Where(m => m.Timestamp >= cutoff)
                        );

                        _metricsStore.TryUpdate(key, newQueue, queue);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metrics cleanup");
            }
            finally
            {
                _cleanupLock.Release();
            }
        }
    }
}