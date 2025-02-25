using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class MetricsCollectionService : BackgroundService, ICacheEventListener
    {
        private readonly TimeSeriesMetricsStore _metricsStore;
        private readonly MetricsSettings _settings;
        private readonly Dictionary<string, long> _queryHits = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _queryMisses = new Dictionary<string, long>();
        private readonly Dictionary<string, double> _responseTimesMs = new Dictionary<string, double>();
        private readonly Dictionary<string, MetricValue> _customMetrics = new Dictionary<string, MetricValue>();
        
        public MetricsCollectionService(
            TimeSeriesMetricsStore metricsStore = null,
            IOptions<MetricsSettings> settings = null)
        {
            _metricsStore = metricsStore ?? new TimeSeriesMetricsStore();
            _settings = settings?.Value ?? new MetricsSettings();
        }
        
        public async Task RecordQueryMetrics(string query, double responseTimeMs, bool hit)
        {
            // Record query stats
            if (hit)
            {
                if (_queryHits.ContainsKey(query))
                    _queryHits[query]++;
                else
                    _queryHits[query] = 1;
            }
            else
            {
                if (_queryMisses.ContainsKey(query))
                    _queryMisses[query]++;
                else
                    _queryMisses[query] = 1;
            }
            
            // Track moving average of response times
            if (_responseTimesMs.ContainsKey(query))
            {
                _responseTimesMs[query] = (_responseTimesMs[query] * 0.9) + (responseTimeMs * 0.1);
            }
            else
            {
                _responseTimesMs[query] = responseTimeMs;
            }
            
            // Store as time series metrics
            await _metricsStore.StoreMetricAsync(
                "query.response_time",
                responseTimeMs,
                DateTime.UtcNow,
                new Dictionary<string, string> { ["query"] = query, ["hit"] = hit.ToString() });
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Record global metrics every minute
                    await RecordGlobalMetrics();
                    
                    // Wait for next collection interval
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error in metrics collection: {ex.Message}");
                }
            }
        }
        
        private async Task RecordGlobalMetrics()
        {
            // Calculate hit rate
            long totalHits = 0;
            long totalMisses = 0;
            
            foreach (var hits in _queryHits.Values)
                totalHits += hits;
                
            foreach (var misses in _queryMisses.Values)
                totalMisses += misses;
                
            var hitRate = totalHits + totalMisses > 0
                ? (double)totalHits / (totalHits + totalMisses)
                : 0;
                
            // Calculate average response time
            double avgResponseTime = 0;
            if (_responseTimesMs.Count > 0)
            {
                double sum = 0;
                foreach (var time in _responseTimesMs.Values)
                    sum += time;
                    
                avgResponseTime = sum / _responseTimesMs.Count;
            }
            
            // Record global metrics
            await _metricsStore.StoreMetricAsync("cache.hit_rate", hitRate, DateTime.UtcNow);
            await _metricsStore.StoreMetricAsync("cache.avg_response_time", avgResponseTime, DateTime.UtcNow);
            await _metricsStore.StoreMetricAsync("cache.query_count", totalHits + totalMisses, DateTime.UtcNow);
            await _metricsStore.StoreMetricAsync("cache.memory_usage_mb", GetMemoryUsage(), DateTime.UtcNow);
            
            // Record CPU usage for adaptive compression
            await _metricsStore.StoreMetricAsync("system.cpu_usage_percent", GetCpuUsage(), DateTime.UtcNow);
            
            // Record compression metrics if available
            foreach (var metric in _customMetrics)
            {
                if (metric.Key.StartsWith("compression"))
                {
                    await _metricsStore.StoreMetricAsync(
                        $"cache.{metric.Key}", 
                        metric.Value.LastValue, 
                        DateTime.UtcNow);
                }
            }
        }
        
        private double GetMemoryUsage()
        {
            // This is a simplified implementation that just returns the current process memory
            var process = System.Diagnostics.Process.GetCurrentProcess();
            return process.WorkingSet64 / (1024.0 * 1024.0);
        }
        
        private double GetCpuUsage()
        {
            try
            {
                var cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue(); // First call will always return 0
                Thread.Sleep(100); // Wait a bit for measurement
                return cpuCounter.NextValue();
            }
            catch (Exception)
            {
                // Fallback to a default value if performance counter is not available
                return 50.0; // Assume 50% CPU usage
            }
        }
        
        public async Task RecordCacheWriteMetric(string key, double responseTimeMs)
        {
            await _metricsStore.StoreMetricAsync(
                "cache.write_time",
                responseTimeMs,
                DateTime.UtcNow,
                new Dictionary<string, string> { ["key"] = key });
        }
        
        public async Task RecordCustomMetric(string metricName, double value)
        {
            // Store custom metrics for later aggregation
            if (!_customMetrics.TryGetValue(metricName, out var metric))
            {
                metric = new MetricValue();
                _customMetrics[metricName] = metric;
            }
            
            // Update metric with new value using exponential moving average
            metric.Update(value);
            
            // For compression metrics, store in time series as well
            if (metricName.StartsWith("compression") || 
                metricName.StartsWith("decompression") ||
                metricName.StartsWith("bytes_saved"))
            {
                await _metricsStore.StoreMetricAsync(
                    $"cache.{metricName}",
                    value,
                    DateTime.UtcNow);
            }
        }
        
        public MetricValue GetCustomMetric(string metricName)
        {
            return _customMetrics.TryGetValue(metricName, out var metric) ? metric : null;
        }
        
        // ICacheEventListener implementation
        public Task OnCacheAccess(string key, double responseTime, bool isHit)
        {
            return RecordQueryMetrics(key, responseTime, isHit);
        }
        
        public Task OnCacheEviction(string key)
        {
            return _metricsStore.StoreMetricAsync(
                "cache.eviction",
                1,
                DateTime.UtcNow,
                new Dictionary<string, string> { ["key"] = key });
        }
        
        public Task OnCachePreWarm(string key, bool success)
        {
            return _metricsStore.StoreMetricAsync(
                "cache.prewarm",
                success ? 1 : 0,
                DateTime.UtcNow,
                new Dictionary<string, string> { ["key"] = key });
        }
    }
}