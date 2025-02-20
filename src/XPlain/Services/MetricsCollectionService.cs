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
        private readonly ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>> _metricsStore;
        private readonly ConcurrentDictionary<string, long> _queryCounters;
        private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(24);
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _cleanupLock = new SemaphoreSlim(1, 1);

        public MetricsCollectionService(ILogger<MetricsCollectionService> logger)
        {
            _logger = logger;
            _metricsStore = new ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>>();
            _queryCounters = new ConcurrentDictionary<string, long>();
            _cleanupTimer = new Timer(CleanupOldMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task RecordQueryMetrics(string key, double responseTime, bool isHit)
        {
            try
            {
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
                var cutoff = DateTime.UtcNow - window;
                var totalRequests = _metricsStore.Values
                    .SelectMany(q => q.Where(m => m.Timestamp >= cutoff))
                    .Sum(m => m.RequestCount);

                // Normalize activity level between 0 and 1
                // Assuming 1000 requests per hour is "high" activity
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
                var cutoff = DateTime.UtcNow - _retentionPeriod;

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