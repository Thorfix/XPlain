using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class MetricsCollectionService : IHostedService, IDisposable
    {
        private readonly ILogger<MetricsCollectionService> _logger;
        private readonly TimeSeriesMetricsStore _timeSeriesStore;
        private readonly MetricsSettings _settings;
        
        // In-memory sliding windows for real-time aggregation
        private readonly ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>> _recentMetrics;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>> _frequencyMetrics;
        private readonly ConcurrentQueue<MetricsRecord> _activityMetrics;
        
        private readonly SemaphoreSlim _cleanupLock = new SemaphoreSlim(1, 1);
        private Timer? _cleanupTimer;
        private bool _disposed;

        public MetricsCollectionService(
            ILogger<MetricsCollectionService> logger,
            TimeSeriesMetricsStore timeSeriesStore,
            IOptions<MetricsSettings> settings)
        {
            _logger = logger;
            _timeSeriesStore = timeSeriesStore;
            _settings = settings.Value;
            
            _recentMetrics = new ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>>();
            _frequencyMetrics = new ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>>();
            _activityMetrics = new ConcurrentQueue<MetricsRecord>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Metrics Collection Service is starting.");
            
            _cleanupTimer = new Timer(
                CleanupOldMetrics,
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(_settings.CleanupIntervalMinutes));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Metrics Collection Service is stopping.");

            _cleanupTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public async Task RecordQueryMetrics(string key, double responseTime, bool isHit)
        {
            try
            {
                var timestamp = DateTime.UtcNow;
                var metrics = new MetricsRecord
                {
                    Timestamp = timestamp,
                    ResponseTime = responseTime,
                    IsHit = isHit,
                    Key = key
                };

                // Update real-time sliding windows
                UpdateSlidingWindows(metrics);

                // Persist to time series database
                await _timeSeriesStore.StoreQueryMetric(key, responseTime, isHit, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording metrics for key {Key}", key);
            }
        }

        private void UpdateSlidingWindows(MetricsRecord metrics)
        {
            // Update recent metrics (for response time and hit rate)
            var recentQueue = _recentMetrics.GetOrAdd(metrics.Key, _ => new ConcurrentQueue<MetricsRecord>());
            recentQueue.Enqueue(metrics);

            // Update frequency metrics
            var frequencyQueue = _frequencyMetrics.GetOrAdd(metrics.Key, _ => new ConcurrentQueue<MetricsRecord>());
            frequencyQueue.Enqueue(metrics);

            // Update activity metrics
            _activityMetrics.Enqueue(metrics);
        }

        public async Task<double> GetQueryFrequency(string key)
        {
            try
            {
                var window = TimeSpan.FromMinutes(_settings.QueryFrequencyWindowMinutes);

                // Try time series store first
                var storedFrequency = await _timeSeriesStore.GetQueryFrequency(key, window);
                if (storedFrequency > 0) return storedFrequency;

                // Fall back to in-memory sliding window
                var cutoff = DateTime.UtcNow - window;
                if (_frequencyMetrics.TryGetValue(key, out var queue))
                {
                    var count = queue.Count(m => m.Timestamp >= cutoff);
                    return count / window.TotalHours;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting query frequency for key {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetAverageResponseTime(string key)
        {
            try
            {
                var window = TimeSpan.FromMinutes(_settings.ResponseTimeWindowMinutes);

                // Try time series store first
                var storedAverage = await _timeSeriesStore.GetAverageResponseTime(key, window);
                if (storedAverage > 0) return storedAverage;

                // Fall back to in-memory sliding window
                var cutoff = DateTime.UtcNow - window;
                if (_recentMetrics.TryGetValue(key, out var queue))
                {
                    var metrics = queue.Where(m => m.Timestamp >= cutoff).ToList();
                    if (metrics.Any())
                    {
                        return metrics.Average(m => m.ResponseTime);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting average response time for key {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetCacheHitRate(string key)
        {
            try
            {
                var window = TimeSpan.FromMinutes(_settings.HitRateWindowMinutes);

                // Try time series store first
                var storedHitRate = await _timeSeriesStore.GetCacheHitRate(key, window);
                if (storedHitRate >= 0) return storedHitRate;

                // Fall back to in-memory sliding window
                var cutoff = DateTime.UtcNow - window;
                if (_recentMetrics.TryGetValue(key, out var queue))
                {
                    var metrics = queue.Where(m => m.Timestamp >= cutoff).ToList();
                    if (metrics.Any())
                    {
                        return (double)metrics.Count(m => m.IsHit) / metrics.Count;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache hit rate for key {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetUserActivityLevel()
        {
            try
            {
                var window = TimeSpan.FromMinutes(_settings.UserActivityWindowMinutes);

                // Try time series store first
                var storedActivityLevel = await _timeSeriesStore.GetUserActivityLevel(window);
                if (storedActivityLevel >= 0) return storedActivityLevel;

                // Fall back to in-memory sliding window
                var cutoff = DateTime.UtcNow - window;
                var recentRequests = _activityMetrics.Count(m => m.Timestamp >= cutoff);
                var requestsPerHour = recentRequests / window.TotalHours;

                // Normalize to 0-1 range (1000 requests/hour considered high activity)
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
                var now = DateTime.UtcNow;

                // Cleanup recent metrics
                var recentWindow = TimeSpan.FromMinutes(_settings.ResponseTimeWindowMinutes);
                await CleanupQueue(_recentMetrics, now - recentWindow);

                // Cleanup frequency metrics
                var frequencyWindow = TimeSpan.FromMinutes(_settings.QueryFrequencyWindowMinutes);
                await CleanupQueue(_frequencyMetrics, now - frequencyWindow);

                // Cleanup activity metrics
                var activityWindow = TimeSpan.FromMinutes(_settings.UserActivityWindowMinutes);
                await CleanupSingleQueue(_activityMetrics, now - activityWindow);

                // Cleanup persisted metrics
                await _timeSeriesStore.CleanupOldMetrics();
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

        private async Task CleanupQueue(
            ConcurrentDictionary<string, ConcurrentQueue<MetricsRecord>> queues,
            DateTime cutoff)
        {
            foreach (var key in queues.Keys)
            {
                if (queues.TryGetValue(key, out var queue))
                {
                    await CleanupSingleQueue(queue, cutoff);
                }
            }
        }

        private async Task CleanupSingleQueue(ConcurrentQueue<MetricsRecord> queue, DateTime cutoff)
        {
            var newQueue = new ConcurrentQueue<MetricsRecord>(
                queue.Where(m => m.Timestamp >= cutoff)
            );

            while (queue.TryDequeue(out _)) { }
            foreach (var item in newQueue)
            {
                queue.Enqueue(item);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _cleanupTimer?.Dispose();
                _cleanupLock.Dispose();
            }

            _disposed = true;
        }
    }

    public class MetricsRecord
    {
        public DateTime Timestamp { get; set; }
        public string Key { get; set; } = "";
        public double ResponseTime { get; set; }
        public bool IsHit { get; set; }
    }
}