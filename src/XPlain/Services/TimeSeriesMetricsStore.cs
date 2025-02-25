using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class MetricDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }

    public class TimeSeriesMetricsStore
    {
        private readonly Dictionary<string, List<MetricDataPoint>> _metrics = new();
        private readonly Dictionary<string, TimeSpan> _retentionPeriods = new();
        private readonly object _lock = new();
        
        // Default retention period is 7 days
        private readonly TimeSpan _defaultRetentionPeriod = TimeSpan.FromDays(7);
        
        public async Task RecordMetricAsync(string metricName, double value)
        {
            lock (_lock)
            {
                if (!_metrics.TryGetValue(metricName, out var dataPoints))
                {
                    dataPoints = new List<MetricDataPoint>();
                    _metrics[metricName] = dataPoints;
                }
                
                dataPoints.Add(new MetricDataPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = value
                });
                
                // Enforce retention policy
                EnforceRetention(metricName);
            }
        }
        
        public async Task RecordMetricBatchAsync(string metricName, IEnumerable<(DateTime timestamp, double value)> dataPoints)
        {
            lock (_lock)
            {
                if (!_metrics.TryGetValue(metricName, out var existingPoints))
                {
                    existingPoints = new List<MetricDataPoint>();
                    _metrics[metricName] = existingPoints;
                }
                
                foreach (var (timestamp, value) in dataPoints)
                {
                    existingPoints.Add(new MetricDataPoint
                    {
                        Timestamp = timestamp,
                        Value = value
                    });
                }
                
                // Sort by timestamp
                _metrics[metricName] = existingPoints.OrderBy(p => p.Timestamp).ToList();
                
                // Enforce retention policy
                EnforceRetention(metricName);
            }
        }
        
        public async Task<List<MetricDataPoint>> GetMetricHistoryAsync(string metricName, TimeSpan period)
        {
            var cutoff = DateTime.UtcNow - period;
            
            lock (_lock)
            {
                if (!_metrics.TryGetValue(metricName, out var dataPoints))
                {
                    return new List<MetricDataPoint>();
                }
                
                return dataPoints
                    .Where(p => p.Timestamp >= cutoff)
                    .OrderBy(p => p.Timestamp)
                    .ToList();
            }
        }
        
        public async Task<Dictionary<string, double>> GetLatestValuesAsync(IEnumerable<string> metricNames)
        {
            var result = new Dictionary<string, double>();
            
            lock (_lock)
            {
                foreach (var metricName in metricNames)
                {
                    if (_metrics.TryGetValue(metricName, out var dataPoints) && dataPoints.Count > 0)
                    {
                        result[metricName] = dataPoints.OrderByDescending(p => p.Timestamp).First().Value;
                    }
                }
            }
            
            return result;
        }
        
        public async Task<double?> GetAggregateAsync(string metricName, TimeSpan period, string aggregation)
        {
            var dataPoints = await GetMetricHistoryAsync(metricName, period);
            
            if (dataPoints.Count == 0)
            {
                return null;
            }
            
            var values = dataPoints.Select(p => p.Value);
            
            return aggregation.ToLowerInvariant() switch
            {
                "avg" or "average" => values.Average(),
                "min" => values.Min(),
                "max" => values.Max(),
                "sum" => values.Sum(),
                "count" => values.Count(),
                "p50" or "median" => Percentile(values, 0.5),
                "p90" => Percentile(values, 0.9),
                "p95" => Percentile(values, 0.95),
                "p99" => Percentile(values, 0.99),
                _ => throw new ArgumentException($"Unsupported aggregation: {aggregation}")
            };
        }
        
        public async Task<Dictionary<string, double>> GetAggregatesAsync(
            IEnumerable<string> metricNames, 
            TimeSpan period, 
            string aggregation)
        {
            var result = new Dictionary<string, double>();
            
            foreach (var metricName in metricNames)
            {
                var value = await GetAggregateAsync(metricName, period, aggregation);
                if (value.HasValue)
                {
                    result[metricName] = value.Value;
                }
            }
            
            return result;
        }
        
        public async Task SetRetentionPolicyAsync(string metricName, TimeSpan retentionPeriod)
        {
            lock (_lock)
            {
                _retentionPeriods[metricName] = retentionPeriod;
                EnforceRetention(metricName);
            }
        }
        
        public async Task<List<string>> GetMetricNamesAsync()
        {
            lock (_lock)
            {
                return _metrics.Keys.ToList();
            }
        }
        
        private void EnforceRetention(string metricName)
        {
            if (!_metrics.TryGetValue(metricName, out var dataPoints))
            {
                return;
            }
            
            var retentionPeriod = _retentionPeriods.TryGetValue(metricName, out var period)
                ? period
                : _defaultRetentionPeriod;
                
            var cutoff = DateTime.UtcNow - retentionPeriod;
            
            _metrics[metricName] = dataPoints.Where(p => p.Timestamp >= cutoff).ToList();
        }
        
        private static double Percentile(IEnumerable<double> values, double percentile)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Max(0, index)];
        }
    }
}