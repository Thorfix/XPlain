using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class TimeSeriesMetricsStore
    {
        private readonly ILogger<TimeSeriesMetricsStore> _logger;
        private readonly MetricsSettings _settings;
        private readonly string _metricsDirectory;
        private readonly Dictionary<string, List<TimeSeriesEntry>> _cachedMetrics = new();
        private readonly object _cacheLock = new();

        public TimeSeriesMetricsStore(
            ILogger<TimeSeriesMetricsStore> logger,
            IOptions<MetricsSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            
            // Create metrics directory if it doesn't exist
            _metricsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XPlain", "Metrics");
                
            Directory.CreateDirectory(_metricsDirectory);
        }

        public async Task StoreMetricsAsync(string metricType, Dictionary<string, object> metrics)
        {
            try
            {
                var entry = new TimeSeriesEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Values = metrics
                };

                // Store in memory cache
                lock (_cacheLock)
                {
                    if (!_cachedMetrics.TryGetValue(metricType, out var entries))
                    {
                        entries = new List<TimeSeriesEntry>();
                        _cachedMetrics[metricType] = entries;
                    }

                    entries.Add(entry);

                    // Keep in-memory cache bounded
                    if (entries.Count > 1000)
                    {
                        entries.RemoveRange(0, entries.Count - 1000);
                    }
                }

                // Write to disk as well
                await PersistMetricsAsync(metricType, entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing metrics");
            }
        }

        public async Task<List<TimeSeriesEntry>> GetMetricsAsync(
            string metricType, 
            DateTime startTime, 
            DateTime endTime)
        {
            // First check the in-memory cache
            List<TimeSeriesEntry> cachedEntries;
            lock (_cacheLock)
            {
                if (_cachedMetrics.TryGetValue(metricType, out var entries))
                {
                    cachedEntries = entries
                        .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
                        .ToList();
                    
                    if (cachedEntries.Count > 0)
                    {
                        return cachedEntries;
                    }
                }
            }

            // If not in cache or empty range, load from disk
            return await LoadMetricsFromDiskAsync(metricType, startTime, endTime);
        }

        public async Task CleanupOldMetricsAsync(TimeSpan retentionPeriod)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow - retentionPeriod;
                var metricsRoot = new DirectoryInfo(_metricsDirectory);
                
                foreach (var typeDir in metricsRoot.GetDirectories())
                {
                    foreach (var dayDir in typeDir.GetDirectories())
                    {
                        if (DateTime.TryParse(dayDir.Name, out var dirDate) && dirDate < cutoffDate)
                        {
                            dayDir.Delete(true);
                            _logger.LogInformation($"Deleted old metrics: {typeDir.Name}/{dayDir.Name}");
                        }
                    }
                }

                // Also clean up in-memory cache
                lock (_cacheLock)
                {
                    foreach (var metricType in _cachedMetrics.Keys.ToList())
                    {
                        _cachedMetrics[metricType] = _cachedMetrics[metricType]
                            .Where(e => e.Timestamp >= cutoffDate)
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old metrics");
            }
        }

        private async Task PersistMetricsAsync(string metricType, TimeSeriesEntry entry)
        {
            var day = entry.Timestamp.ToString("yyyy-MM-dd");
            var hour = entry.Timestamp.Hour;
            
            var dirPath = Path.Combine(_metricsDirectory, metricType, day);
            Directory.CreateDirectory(dirPath);
            
            var filePath = Path.Combine(dirPath, $"{hour}.jsonl");
            var line = JsonSerializer.Serialize(entry);
            
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine);
        }

        private async Task<List<TimeSeriesEntry>> LoadMetricsFromDiskAsync(
            string metricType, 
            DateTime startTime, 
            DateTime endTime)
        {
            var result = new List<TimeSeriesEntry>();
            var metricDir = Path.Combine(_metricsDirectory, metricType);
            
            if (!Directory.Exists(metricDir))
                return result;

            // Get date range to scan
            var startDay = startTime.Date;
            var endDay = endTime.Date;
            var currentDay = startDay;
            
            while (currentDay <= endDay)
            {
                var dayStr = currentDay.ToString("yyyy-MM-dd");
                var dayPath = Path.Combine(metricDir, dayStr);
                
                if (Directory.Exists(dayPath))
                {
                    // Find all hourly files for this day
                    foreach (var file in Directory.GetFiles(dayPath, "*.jsonl"))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (int.TryParse(fileName, out var hour))
                        {
                            // Check if hour is in our time range
                            var fileTime = currentDay.AddHours(hour);
                            if (fileTime >= startTime && fileTime <= endTime)
                            {
                                try
                                {
                                    // Read and parse file
                                    var lines = await File.ReadAllLinesAsync(file);
                                    foreach (var line in lines)
                                    {
                                        if (string.IsNullOrWhiteSpace(line))
                                            continue;
                                            
                                        var entry = JsonSerializer.Deserialize<TimeSeriesEntry>(line);
                                        if (entry != null && entry.Timestamp >= startTime && entry.Timestamp <= endTime)
                                        {
                                            result.Add(entry);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error reading metrics file: {file}");
                                }
                            }
                        }
                    }
                }
                
                currentDay = currentDay.AddDays(1);
            }
            
            return result.OrderBy(e => e.Timestamp).ToList();
        }
    }

    public class TimeSeriesEntry
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Values { get; set; } = new();
    }
}