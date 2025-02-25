using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class MetricDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }

    public class TimeSeriesMetricsStore
    {
        private readonly ILogger<TimeSeriesMetricsStore> _logger;
        private readonly Dictionary<string, List<MetricDataPoint>> _metrics = new();
        private readonly Dictionary<string, Dictionary<string, string>> _metricTags = new();
        private readonly object _lock = new();
        private readonly MetricsSettings _settings;

        public TimeSeriesMetricsStore(
            ILogger<TimeSeriesMetricsStore> logger = null,
            IOptions<MetricsSettings> settings = null)
        {
            _logger = logger ?? new Logger<TimeSeriesMetricsStore>(new LoggerFactory());
            _settings = settings?.Value ?? new MetricsSettings();
        }

        public async Task StoreMetricAsync(string metricName, double value, DateTime timestamp, Dictionary<string, string> tags = null)
        {
            await RecordMetricAsync(metricName, value, tags, timestamp);
        }
        
        public async Task RecordMetricAsync(string metricName, double value, Dictionary<string, string> tags = null, DateTime? timestamp = null)
        {
            try
            {
                var now = timestamp ?? DateTime.UtcNow;
                
                lock (_lock)
                {
                    if (!_metrics.TryGetValue(metricName, out var points))
                    {
                        points = new List<MetricDataPoint>();
                        _metrics[metricName] = points;
                    }
                    
                    points.Add(new MetricDataPoint
                    {
                        Timestamp = now,
                        Value = value
                    });
                    
                    // Store tags if provided
                    if (tags != null && tags.Count > 0)
                    {
                        if (!_metricTags.TryGetValue(metricName, out var existingTags))
                        {
                            existingTags = new Dictionary<string, string>();
                            _metricTags[metricName] = existingTags;
                        }
                        
                        foreach (var tag in tags)
                        {
                            existingTags[tag.Key] = tag.Value;
                        }
                    }
                    
                    // Keep only the most recent data points based on retention policy
                    if (points.Count > _settings.MetricDataPointsRetention)
                    {
                        var excess = points.Count - _settings.MetricDataPointsRetention;
                        points.RemoveRange(0, excess);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording metric {metricName}: {ex.Message}");
            }
        }

        public async Task<List<MetricDataPoint>> GetMetricHistoryAsync(string metricName, TimeSpan period)
        {
            try
            {
                var cutoff = DateTime.UtcNow - period;
                
                lock (_lock)
                {
                    if (!_metrics.TryGetValue(metricName, out var points))
                    {
                        return new List<MetricDataPoint>();
                    }
                    
                    return points
                        .Where(p => p.Timestamp >= cutoff)
                        .OrderBy(p => p.Timestamp)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving metric history for {metricName}: {ex.Message}");
                return new List<MetricDataPoint>();
            }
        }

        public async Task<double> GetAverageValueAsync(string metricName, TimeSpan period)
        {
            try
            {
                var history = await GetMetricHistoryAsync(metricName, period);
                return history.Any() ? history.Average(p => p.Value) : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating average for {metricName}: {ex.Message}");
                return 0;
            }
        }

        public async Task<Dictionary<string, double>> GetLatestValuesAsync(IEnumerable<string> metricNames)
        {
            var result = new Dictionary<string, double>();
            
            foreach (var metricName in metricNames)
            {
                try
                {
                    lock (_lock)
                    {
                        if (_metrics.TryGetValue(metricName, out var points) && points.Any())
                        {
                            result[metricName] = points.Last().Value;
                        }
                        else
                        {
                            result[metricName] = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error retrieving latest value for {metricName}: {ex.Message}");
                    result[metricName] = 0;
                }
            }
            
            return result;
        }

        public async Task<bool> ClearMetricHistoryAsync(string metricName)
        {
            try
            {
                lock (_lock)
                {
                    return _metrics.Remove(metricName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error clearing metric history for {metricName}: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetAvailableMetricsAsync()
        {
            try
            {
                lock (_lock)
                {
                    return _metrics.Keys.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving available metrics: {ex.Message}");
                return new List<string>();
            }
        }
        
        public async Task<Dictionary<string, string>> GetMetricTagsAsync(string metricName)
        {
            try
            {
                lock (_lock)
                {
                    if (_metricTags.TryGetValue(metricName, out var tags))
                    {
                        return new Dictionary<string, string>(tags);
                    }
                    
                    return new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving tags for metric {metricName}: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }
    }
}