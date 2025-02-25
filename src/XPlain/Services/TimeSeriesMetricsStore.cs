using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class TimeSeriesMetricsStore
    {
        private readonly Dictionary<string, List<TimeSeriesDataPoint>> _metricsData = new Dictionary<string, List<TimeSeriesDataPoint>>();
        
        public Task StoreMetricAsync(string metricName, double value, DateTime timestamp, Dictionary<string, string> tags = null)
        {
            if (!_metricsData.ContainsKey(metricName))
            {
                _metricsData[metricName] = new List<TimeSeriesDataPoint>();
            }
            
            _metricsData[metricName].Add(new TimeSeriesDataPoint
            {
                Timestamp = timestamp,
                Value = value,
                Tags = tags ?? new Dictionary<string, string>()
            });
            
            return Task.CompletedTask;
        }
        
        public Task<List<TimeSeriesDataPoint>> GetMetricDataAsync(string metricName, DateTime startTime, DateTime endTime)
        {
            if (!_metricsData.TryGetValue(metricName, out var data))
            {
                return Task.FromResult(new List<TimeSeriesDataPoint>());
            }
            
            return Task.FromResult(data.FindAll(d => d.Timestamp >= startTime && d.Timestamp <= endTime));
        }
        
        public Task<Dictionary<string, double>> GetLatestMetricsAsync(IEnumerable<string> metricNames)
        {
            var result = new Dictionary<string, double>();
            
            foreach (var metricName in metricNames)
            {
                if (_metricsData.TryGetValue(metricName, out var data) && data.Count > 0)
                {
                    data.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                    result[metricName] = data[0].Value;
                }
                else
                {
                    result[metricName] = 0;
                }
            }
            
            return Task.FromResult(result);
        }
    }
    
    public class TimeSeriesDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}