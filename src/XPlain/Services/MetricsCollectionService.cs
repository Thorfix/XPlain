using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class MetricsCollectionService : BackgroundService, ICacheEventListener
    {
        private readonly ILogger<MetricsCollectionService> _logger;
        private readonly TimeSeriesMetricsStore _metricsStore;
        private readonly MetricsSettings _settings;
        private readonly Dictionary<string, QueryMetrics> _queryMetrics = new();
        private readonly object _metricsLock = new();

        public MetricsCollectionService(
            ILogger<MetricsCollectionService> logger,
            TimeSeriesMetricsStore metricsStore,
            IOptions<MetricsSettings> settings)
        {
            _logger = logger;
            _metricsStore = metricsStore;
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Metrics collection service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CollectAndStoreMetrics();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error collecting metrics");
                }

                // Wait for next collection interval
                await Task.Delay(TimeSpan.FromMinutes(_settings.CleanupIntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("Metrics collection service stopped");
        }

        private async Task CollectAndStoreMetrics()
        {
            // Collect current metrics
            var metrics = new Dictionary<string, object>();
            lock (_metricsLock)
            {
                // Compute aggregated metrics
                var totalQueries = 0;
                var totalResponseTime = 0.0;
                var cacheHits = 0;

                foreach (var query in _queryMetrics.Values)
                {
                    totalQueries += query.Count;
                    totalResponseTime += query.TotalResponseTime;
                    cacheHits += query.HitCount;
                }

                // Add to metrics collection
                metrics["query_count"] = totalQueries;
                metrics["avg_response_time"] = totalQueries > 0 ? totalResponseTime / totalQueries : 0;
                metrics["cache_hit_rate"] = totalQueries > 0 ? (double)cacheHits / totalQueries : 0;
                metrics["unique_queries"] = _queryMetrics.Count;
            }

            // Store metrics
            await _metricsStore.StoreMetricsAsync("cache_performance", metrics);

            // Clean up old metrics data
            await _metricsStore.CleanupOldMetricsAsync(TimeSpan.FromDays(_settings.DefaultRetentionDays));
        }

        public async Task RecordQueryMetrics(string key, double responseTime, bool isHit)
        {
            lock (_metricsLock)
            {
                if (!_queryMetrics.TryGetValue(key, out var metrics))
                {
                    metrics = new QueryMetrics();
                    _queryMetrics[key] = metrics;
                }

                metrics.Count++;
                metrics.TotalResponseTime += responseTime;
                if (isHit) metrics.HitCount++;
                metrics.LastAccessed = DateTime.UtcNow;
            }
        }

        public async Task OnCacheAccess(string key, double responseTime, bool isHit)
        {
            await RecordQueryMetrics(key, responseTime, isHit);

            // Store individual access metrics
            var metrics = new Dictionary<string, object>
            {
                ["key"] = key,
                ["response_time"] = responseTime,
                ["hit"] = isHit
            };

            await _metricsStore.StoreMetricsAsync("cache_access", metrics);
        }

        public async Task OnCacheEviction(string key)
        {
            var metrics = new Dictionary<string, object>
            {
                ["key"] = key,
                ["timestamp"] = DateTime.UtcNow
            };

            await _metricsStore.StoreMetricsAsync("cache_eviction", metrics);
        }

        public async Task OnCachePreWarm(string key, bool success)
        {
            var metrics = new Dictionary<string, object>
            {
                ["key"] = key,
                ["success"] = success,
                ["timestamp"] = DateTime.UtcNow
            };

            await _metricsStore.StoreMetricsAsync("cache_prewarm", metrics);
        }

        private class QueryMetrics
        {
            public int Count { get; set; }
            public double TotalResponseTime { get; set; }
            public int HitCount { get; set; }
            public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
        }
    }
}