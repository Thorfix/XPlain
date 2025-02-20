using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class TimeSeriesMetricsStore
    {
        private readonly ILogger<TimeSeriesMetricsStore> _logger;
        private readonly MetricsSettings _settings;
        private readonly InfluxDBClient _client;
        private readonly string _bucket;

        public TimeSeriesMetricsStore(
            ILogger<TimeSeriesMetricsStore> logger,
            IOptions<MetricsSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            _bucket = _settings.DatabaseName;
            _client = InfluxDBClientFactory.Create(
                _settings.TimeSeriesConnectionString,
                _bucket,
                _settings.DefaultRetentionDays);
        }

        public async Task StoreQueryMetric(string key, double responseTime, bool isHit, DateTime timestamp)
        {
            try
            {
                var point = PointData
                    .Measurement("query_metrics")
                    .Tag("key", key)
                    .Tag("hit", isHit.ToString())
                    .Field("response_time", responseTime)
                    .Timestamp(timestamp, WritePrecision.Ms);

                using var writeApi = _client.GetWriteApiAsync();
                await writeApi.WritePointAsync(point);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store query metric for key {Key}", key);
                throw;
            }
        }

        public async Task StoreEvictionEvent(string key, DateTime timestamp)
        {
            try
            {
                var point = PointData
                    .Measurement("cache_events")
                    .Tag("key", key)
                    .Tag("type", "eviction")
                    .Field("count", 1)
                    .Timestamp(timestamp, WritePrecision.Ms);

                using var writeApi = _client.GetWriteApiAsync();
                await writeApi.WritePointAsync(point);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store eviction event for key {Key}", key);
                throw;
            }
        }

        public async Task StorePreWarmEvent(string key, bool success, DateTime timestamp)
        {
            try
            {
                var point = PointData
                    .Measurement("cache_events")
                    .Tag("key", key)
                    .Tag("type", "prewarm")
                    .Tag("success", success.ToString())
                    .Field("count", 1)
                    .Timestamp(timestamp, WritePrecision.Ms);

                using var writeApi = _client.GetWriteApiAsync();
                await writeApi.WritePointAsync(point);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store pre-warm event for key {Key}", key);
                throw;
            }
        }

        public async Task<double> GetQueryFrequency(string key, TimeSpan window)
        {
            try
            {
                var query = $@"
                    from(bucket: ""{_bucket}"")
                    |> range(start: -{window.TotalSeconds}s)
                    |> filter(fn: (r) => r[""_measurement""] == ""query_metrics"" 
                                    and r[""key""] == ""{key}"")
                    |> count()";

                using var queryApi = _client.GetQueryApi();
                var result = await queryApi.QueryAsync<double>(query);
                var count = result?.FirstOrDefault() ?? 0;
                return count / window.TotalHours;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get query frequency for key {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetAverageResponseTime(string key, TimeSpan window)
        {
            try
            {
                var query = $@"
                    from(bucket: ""{_bucket}"")
                    |> range(start: -{window.TotalSeconds}s)
                    |> filter(fn: (r) => r[""_measurement""] == ""query_metrics"" 
                                    and r[""key""] == ""{key}"")
                    |> mean(column: ""response_time"")";

                using var queryApi = _client.GetQueryApi();
                var result = await queryApi.QueryAsync<double>(query);
                return result?.FirstOrDefault() ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get average response time for key {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetCacheHitRate(string key, TimeSpan window)
        {
            try
            {
                var query = $@"
                    from(bucket: ""{_bucket}"")
                    |> range(start: -{window.TotalSeconds}s)
                    |> filter(fn: (r) => r[""_measurement""] == ""query_metrics"" 
                                    and r[""key""] == ""{key}"")
                    |> group(columns: [""hit""])
                    |> count()";

                using var queryApi = _client.GetQueryApi();
                var results = await queryApi.QueryAsync<double>(query);
                
                double hits = 0, total = 0;
                foreach (var result in results)
                {
                    if (result.GetValueByKey("hit").ToString() == "True")
                        hits = result;
                    total += result;
                }

                return total > 0 ? hits / total : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cache hit rate for key {Key}", key);
                return 0;
            }
        }

        public async Task<double> GetUserActivityLevel(TimeSpan window)
        {
            try
            {
                var query = $@"
                    from(bucket: ""{_bucket}"")
                    |> range(start: -{window.TotalSeconds}s)
                    |> filter(fn: (r) => r[""_measurement""] == ""query_metrics"")
                    |> window(every: 1m)
                    |> count()
                    |> mean()";

                using var queryApi = _client.GetQueryApi();
                var result = await queryApi.QueryAsync<double>(query);
                var avgRequestsPerMinute = result?.FirstOrDefault() ?? 0;
                
                // Normalize to 0-1 range (assuming 1000 requests/minute is high activity)
                return Math.Min(1.0, avgRequestsPerMinute / 1000.0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user activity level", ex);
                return 0;
            }
        }

        public async Task CleanupOldMetrics()
        {
            try
            {
                // InfluxDB handles data retention through its retention policy
                // which is set during bucket creation
                _logger.LogInformation("Cleanup handled by InfluxDB retention policy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metrics cleanup");
            }
        }
    }
}