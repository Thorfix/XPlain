using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace XPlain.Services
{
    public class FileBasedCacheProvider : ICacheProvider
    {
        private readonly CacheSettings _settings;
        private readonly string _cacheDirectory;
        private readonly string _analyticsDirectory;
        private readonly string _walDirectory;
        private readonly string _backupDirectory;
        private long _cacheHits;
        private long _cacheMisses;
        private int _invalidationCount;
        private readonly object _statsLock = new();
        private readonly ConcurrentDictionary<string, string> _keyToFileMap = new();
        private readonly ILLMProvider _llmProvider;
        private readonly ConcurrentDictionary<string, int> _queryTypeStats = new();
        private readonly ConcurrentDictionary<string, (int Count, double TotalTimeCached, double TotalTimeNonCached)> _queryResponseStats = new();
        private readonly ConcurrentDictionary<string, int> _queryFrequency = new();
        private readonly ConcurrentQueue<(DateTime Time, int ItemsInvalidated)> _invalidationHistory = new();
        private readonly string _statsFilePath;
        private readonly string _analyticsFilePath;
        private readonly Timer _analyticsTimer;

        public FileBasedCacheProvider(IOptions<CacheSettings> settings, ILLMProvider llmProvider)
        {
            _settings = settings.Value;
            _llmProvider = llmProvider;
            _cacheDirectory = _settings.CacheDirectory ?? Path.Combine(AppContext.BaseDirectory, "cache");
            _analyticsDirectory = Path.Combine(_cacheDirectory, "analytics");
            _walDirectory = Path.Combine(_cacheDirectory, "wal");
            _backupDirectory = Path.Combine(_cacheDirectory, "backups");
            _statsFilePath = Path.Combine(_cacheDirectory, "cache_stats.json");
            _analyticsFilePath = Path.Combine(_analyticsDirectory, "analytics.json");
            
            Directory.CreateDirectory(_cacheDirectory);
            Directory.CreateDirectory(_analyticsDirectory);
            Directory.CreateDirectory(_walDirectory);
            Directory.CreateDirectory(_backupDirectory);
            
            // Initialize cache state
            InitializeCacheState();
            StartPeriodicBackup();
            LoadStatsFromDisk();

            // Set up periodic analytics logging (every hour)
            _analyticsTimer = new Timer(async _ => await LogAnalyticsAsync(), 
                null, TimeSpan.Zero, TimeSpan.FromHours(1));
        }

        private string GetFilePath(string key)
        {
            return _keyToFileMap.GetOrAdd(key, k =>
            {
                var safeKey = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(k)));
                return Path.Combine(_cacheDirectory, $"{safeKey}.json");
            });
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            if (!_settings.CacheEnabled) return null;

            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                IncrementMisses();
                return null;
            }

            try
            {
                var cacheEntry = await JsonSerializer.DeserializeAsync<CacheEntry<T>>(
                    File.OpenRead(filePath));

                if (cacheEntry == null || cacheEntry.IsExpired)
                {
                    await RemoveAsync(key);
                    IncrementMisses();
                    return null;
                }

                IncrementHits();
                return cacheEntry.Value;
            }
            catch
            {
                await RemoveAsync(key);
                IncrementMisses();
                return null;
            }
        }

        private void StartPeriodicBackup()
        {
            var backupTimer = new Timer(async _ => await CreateBackupAsync(), 
                null, TimeSpan.Zero, TimeSpan.FromHours(6));
        }

        private async Task CreateBackupAsync()
        {
            var backupFileName = $"cache_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
            var backupPath = Path.Combine(_backupDirectory, backupFileName);

            try
            {
                // Create backup archive
                using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
                
                // Add cache files
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
                {
                    archive.CreateEntryFromFile(file, Path.GetFileName(file));
                }

                // Add WAL files
                foreach (var file in Directory.GetFiles(_walDirectory))
                {
                    archive.CreateEntryFromFile(file, $"wal/{Path.GetFileName(file)}");
                }

                // Cleanup old backups (keep last 5)
                var backups = Directory.GetFiles(_backupDirectory)
                    .OrderByDescending(f => f)
                    .Skip(5);
                    
                foreach (var oldBackup in backups)
                {
                    try { File.Delete(oldBackup); } catch { }
                }
            }
            catch
            {
                // Log backup failure
            }
        }

        private async Task<bool> RestoreFromBackupAsync()
        {
            var latestBackup = Directory.GetFiles(_backupDirectory)
                .OrderByDescending(f => f)
                .FirstOrDefault();

            if (latestBackup == null) return false;

            try
            {
                // Clear current cache
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
                {
                    File.Delete(file);
                }

                // Restore from backup
                using var archive = ZipFile.OpenRead(latestBackup);
                foreach (var entry in archive.Entries)
                {
                    var targetPath = entry.FullName.StartsWith("wal/")
                        ? Path.Combine(_walDirectory, Path.GetFileName(entry.FullName))
                        : Path.Combine(_cacheDirectory, entry.FullName);

                    entry.ExtractToFile(targetPath, true);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeCacheState()
        {
            try
            {
                // Verify cache integrity
                if (!VerifyCacheIntegrity())
                {
                    // Try to recover from WAL
                    if (!RecoverFromWAL())
                    {
                        // If WAL recovery fails, try backup
                        RestoreFromBackupAsync().Wait();
                    }
                }
            }
            catch
            {
                // If all recovery methods fail, start with empty cache
                ClearCache();
            }
        }

        private bool VerifyCacheIntegrity()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
                {
                    if (!VerifyFileIntegrity(file))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyFileIntegrity(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var cacheEntry = JsonSerializer.Deserialize<CacheEntryWithChecksum>(stream);
                return cacheEntry?.VerifyChecksum() ?? false;
            }
            catch
            {
                return false;
            }
        }

        private bool RecoverFromWAL()
        {
            try
            {
                var walFiles = Directory.GetFiles(_walDirectory)
                    .OrderBy(f => f)
                    .ToList();

                foreach (var walFile in walFiles)
                {
                    var walEntry = JsonSerializer.Deserialize<WALEntry>(
                        File.ReadAllText(walFile));

                    if (walEntry != null)
                    {
                        ApplyWALEntry(walEntry);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyWALEntry(WALEntry entry)
        {
            var targetPath = GetFilePath(entry.Key);
            
            switch (entry.Operation)
            {
                case WALOperation.Set:
                    File.WriteAllText(targetPath, entry.Data);
                    break;
                case WALOperation.Delete:
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    break;
            }
        }

        private void ClearCache()
        {
            try
            {
                Directory.Delete(_cacheDirectory, true);
                Directory.CreateDirectory(_cacheDirectory);
                Directory.CreateDirectory(_analyticsDirectory);
                Directory.CreateDirectory(_walDirectory);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
        {
            if (!_settings.CacheEnabled) return;

            var filePath = GetFilePath(key);
            var tempFilePath = filePath + ".tmp";
            var cacheEntry = new CacheEntryWithChecksum<T>
            {
                Value = value,
                ExpirationTime = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(_settings.CacheExpirationHours)),
                CodeHash = await CalculateCodeHashAsync()
            };

            // Write to temporary file first
            using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, cacheEntry);
            }

            // Log the operation to WAL
            var walEntry = new WALEntry
            {
                Key = key,
                Operation = WALOperation.Set,
                Data = await File.ReadAllTextAsync(tempFilePath),
                Timestamp = DateTime.UtcNow
            };

            var walFile = Path.Combine(_walDirectory, 
                $"wal_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(walFile, 
                JsonSerializer.Serialize(walEntry));

            // Atomic replace
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            File.Move(tempFilePath, filePath);

            // Cleanup WAL entry after successful write
            try { File.Delete(walFile); } catch { }
        }

        public Task RemoveAsync(string key)
        {
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _keyToFileMap.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            if (!_settings.CacheEnabled) return Task.FromResult(false);
            return Task.FromResult(File.Exists(GetFilePath(key)));
        }

        public async Task InvalidateOnCodeChangeAsync(string newCodeHash)
        {
            if (!_settings.CacheEnabled) return;

            int invalidatedCount = 0;
            var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
            foreach (var file in cacheFiles)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var cacheEntry = await JsonSerializer.DeserializeAsync<CacheEntry<object>>(stream);
                    
                    if (cacheEntry?.CodeHash != newCodeHash)
                    {
                        File.Delete(file);
                        invalidatedCount++;
                    }
                }
                catch
                {
                    // If we can't read the file, delete it
                    File.Delete(file);
                    invalidatedCount++;
                }
            }

            // Record invalidation statistics
            lock (_statsLock)
            {
                _invalidationCount++;
                _invalidationHistory.Enqueue((DateTime.UtcNow, invalidatedCount));
                while (_invalidationHistory.Count > 100) // Keep last 100 invalidations
                {
                    _invalidationHistory.TryDequeue(out _);
                }
            }

            await LogAnalyticsAsync();
        }

        public async Task WarmupCacheAsync(string[] frequentQuestions, string codeContext)
        {
            if (!_settings.CacheEnabled) return;

            foreach (var question in frequentQuestions)
            {
                var key = GetCacheKey(question, codeContext);
                if (!await ExistsAsync(key))
                {
                    var response = await _llmProvider.GetCompletionAsync(
                        $"I have the following code:\n\n{codeContext}\n\nMy question is: {question}");
                    await SetAsync(key, response);
                }
            }
        }

        private string GetCacheKey(string question, string codeContext)
        {
            var input = $"{question}:{codeContext}";
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        }

        private async Task<string> CalculateCodeHashAsync()
        {
            using var sha256 = SHA256.Create();
            var hashBuilder = new StringBuilder();

            foreach (var file in Directory.GetFiles(_settings.CodebasePath, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    hashBuilder.Append(Convert.ToBase64String(hash));
                }
                catch
                {
                    // Skip files that can't be read
                    continue;
                }
            }

            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString())));
        }

        public CacheStats GetCacheStats()
        {
            lock (_statsLock)
            {
                var stats = new CacheStats
                {
                    Hits = _cacheHits,
                    Misses = _cacheMisses,
                    StorageUsageBytes = GetCacheStorageUsage(),
                    CachedItemCount = Directory.GetFiles(_cacheDirectory, "*.json").Length,
                    QueryTypeStats = new Dictionary<string, int>(_queryTypeStats),
                    TopQueries = GetTopQueries(),
                    AverageResponseTimes = _queryResponseStats.ToDictionary(
                        kv => kv.Key,
                        kv => (kv.Value.TotalTimeCached + kv.Value.TotalTimeNonCached) / kv.Value.Count
                    ),
                    PerformanceByQueryType = GetPerformanceMetrics(),
                    InvalidationCount = _invalidationCount,
                    InvalidationHistory = _invalidationHistory.ToList(),
                    LastStatsUpdate = DateTime.UtcNow
                };

                return stats;
            }
        }

        public async Task LogQueryStatsAsync(string queryType, string query, double responseTime, bool wasCached)
        {
            _queryTypeStats.AddOrUpdate(queryType, 1, (_, count) => count + 1);
            _queryFrequency.AddOrUpdate(query, 1, (_, count) => count + 1);
            
            _queryResponseStats.AddOrUpdate(
                queryType,
                (1, wasCached ? responseTime : 0, wasCached ? 0 : responseTime),
                (_, stats) => (
                    stats.Count + 1,
                    stats.TotalTimeCached + (wasCached ? responseTime : 0),
                    stats.TotalTimeNonCached + (wasCached ? 0 : responseTime)
                )
            );

            await SaveStatsToDiskAsync();
        }

        private List<(string Query, int Frequency)> GetTopQueries(int count = 10)
        {
            return _queryFrequency
                .OrderByDescending(kv => kv.Value)
                .Take(count)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        private Dictionary<string, CachePerformanceMetrics> GetPerformanceMetrics()
        {
            return _queryResponseStats.ToDictionary(
                kv => kv.Key,
                kv => new CachePerformanceMetrics
                {
                    CachedResponseTime = kv.Value.TotalTimeCached / Math.Max(1, _cacheHits),
                    NonCachedResponseTime = kv.Value.TotalTimeNonCached / Math.Max(1, _cacheMisses)
                }
            );
        }

        private long GetCacheStorageUsage()
        {
            return Directory.GetFiles(_cacheDirectory, "*.json")
                .Sum(file => new FileInfo(file).Length);
        }

        private async Task SaveStatsToDiskAsync()
        {
            var stats = GetCacheStats();
            await File.WriteAllTextAsync(_statsFilePath, 
                JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
        }

        public async Task LogAnalyticsAsync()
        {
            var analytics = new CacheAnalytics
            {
                Timestamp = DateTime.UtcNow,
                Stats = GetCacheStats(),
                MemoryUsageMB = GetCacheStorageUsage() / 1024.0 / 1024.0,
                RecommendedOptimizations = await GetCacheWarmingRecommendationsAsync()
            };

            var analyticsFile = Path.Combine(_analyticsDirectory, 
                $"analytics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            
            await File.WriteAllTextAsync(analyticsFile, 
                JsonSerializer.Serialize(analytics, new JsonSerializerOptions { WriteIndented = true }));
        }

        public async Task<List<CacheAnalytics>> GetAnalyticsHistoryAsync(DateTime since)
        {
            var files = Directory.GetFiles(_analyticsDirectory, "analytics_*.json")
                .Where(f => File.GetCreationTimeUtc(f) >= since)
                .OrderByDescending(f => f);

            var analytics = new List<CacheAnalytics>();
            foreach (var file in files)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var entry = JsonSerializer.Deserialize<CacheAnalytics>(content);
                    if (entry != null)
                    {
                        analytics.Add(entry);
                    }
                }
                catch
                {
                    // Skip corrupted files
                }
            }
            return analytics;
        }

        public async Task<List<string>> GetCacheWarmingRecommendationsAsync()
        {
            var recommendations = new List<string>();
            var stats = GetCacheStats();
            var analytics = await GetAnalyticsHistoryAsync(DateTime.UtcNow.AddDays(-7));

            // Analyze query patterns
            var frequentUncachedQueries = _queryFrequency
                .Where(q => !File.Exists(GetFilePath(q.Key)))
                .OrderByDescending(q => q.Value)
                .Take(5);

            foreach (var query in frequentUncachedQueries)
            {
                recommendations.Add($"Consider warming cache for frequent query: {query.Key} (used {query.Value} times)");
            }

            // Analyze performance gains
            foreach (var perf in stats.PerformanceByQueryType)
            {
                if (perf.Value.PerformanceGain > 50)
                {
                    recommendations.Add($"Query type '{perf.Key}' shows significant caching benefits ({perf.Value.PerformanceGain:F1}% faster)");
                }
            }

            // Analyze memory trends
            if (analytics.Count >= 2)
            {
                var memoryGrowth = (analytics[0].MemoryUsageMB - analytics[^1].MemoryUsageMB) / analytics.Count;
                if (memoryGrowth > 10)
                {
                    recommendations.Add($"Cache memory usage growing by {memoryGrowth:F1}MB per hour. Consider increasing cleanup frequency.");
                }
            }

            return recommendations;
        }

        public async Task<string> GeneratePerformanceChartAsync(OutputFormat format)
        {
            var analytics = await GetAnalyticsHistoryAsync(DateTime.UtcNow.AddDays(-7));
            var stats = GetCacheStats();

            switch (format)
            {
                case OutputFormat.Markdown:
                    return GenerateMarkdownChart(analytics, stats);
                case OutputFormat.Json:
                    return JsonSerializer.Serialize(new
                    {
                        HistoricalData = analytics,
                        CurrentStats = stats,
                        Charts = new
                        {
                            HitRatio = analytics.Select(a => new { Time = a.Timestamp, Ratio = a.Stats.HitRatio }).ToList(),
                            MemoryUsage = analytics.Select(a => new { Time = a.Timestamp, Usage = a.MemoryUsageMB }).ToList(),
                            PerformanceGains = stats.PerformanceByQueryType.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.PerformanceGain
                            )
                        }
                    }, new JsonSerializerOptions { WriteIndented = true });
                default:
                    return GenerateAsciiChart(analytics, stats);
            }
        }

        private string GenerateMarkdownChart(List<CacheAnalytics> analytics, CacheStats stats)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Cache Performance Charts");
            
            // Hit Ratio Trend
            sb.AppendLine("\n## Hit Ratio Trend");
            sb.AppendLine("```");
            sb.AppendLine(GenerateSparkline(analytics.Select(a => a.Stats.HitRatio)));
            sb.AppendLine("```");

            // Memory Usage Trend
            sb.AppendLine("\n## Memory Usage (MB)");
            sb.AppendLine("```");
            sb.AppendLine(GenerateSparkline(analytics.Select(a => a.MemoryUsageMB)));
            sb.AppendLine("```");

            // Performance Comparison
            sb.AppendLine("\n## Performance Gains by Query Type");
            sb.AppendLine("| Query Type | Speed Improvement |");
            sb.AppendLine("|------------|------------------|");
            foreach (var perf in stats.PerformanceByQueryType)
            {
                sb.AppendLine($"| {perf.Key} | {perf.Value.PerformanceGain:F1}% |");
            }

            return sb.ToString();
        }

        private string GenerateAsciiChart(List<CacheAnalytics> analytics, CacheStats stats)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Cache Performance Charts");
            sb.AppendLine("=======================");
            
            // Hit Ratio Trend
            sb.AppendLine("\nHit Ratio Trend:");
            sb.AppendLine(GenerateSparkline(analytics.Select(a => a.Stats.HitRatio)));

            // Memory Usage Trend
            sb.AppendLine("\nMemory Usage (MB):");
            sb.AppendLine(GenerateSparkline(analytics.Select(a => a.MemoryUsageMB)));

            // Performance Comparison
            sb.AppendLine("\nPerformance Gains by Query Type:");
            foreach (var perf in stats.PerformanceByQueryType)
            {
                sb.AppendLine($"{perf.Key}: {new string('#', (int)(perf.Value.PerformanceGain / 2))} {perf.Value.PerformanceGain:F1}%");
            }

            return sb.ToString();
        }

        private string GenerateSparkline(IEnumerable<double> values)
        {
            const string chars = "▁▂▃▄▅▆▇█";
            var normalized = values.ToList();
            if (!normalized.Any()) return string.Empty;
            
            var min = normalized.Min();
            var max = normalized.Max();
            var range = max - min;
            
            return string.Join("", normalized.Select(v =>
            {
                var idx = range == 0 ? 0 : (int)((v - min) / range * (chars.Length - 1));
                return chars[idx];
            }));
        }

        private void LoadStatsFromDisk()
        {
            if (!File.Exists(_statsFilePath)) return;

            try
            {
                var stats = JsonSerializer.Deserialize<CacheStats>(
                    File.ReadAllText(_statsFilePath));

                if (stats != null)
                {
                    _cacheHits = stats.Hits;
                    _cacheMisses = stats.Misses;
                    _invalidationCount = stats.InvalidationCount;

                    foreach (var (type, count) in stats.QueryTypeStats)
                    {
                        _queryTypeStats.TryAdd(type, count);
                    }

                    foreach (var invalidation in stats.InvalidationHistory)
                    {
                        _invalidationHistory.Enqueue(invalidation);
                    }
                }
            }
            catch
            {
                // Ignore errors loading stats
            }
        }

        private void IncrementHits()
        {
            lock (_statsLock)
            {
                _cacheHits++;
            }
        }

        private void IncrementMisses()
        {
            lock (_statsLock)
            {
                _cacheMisses++;
            }
        }

        private class CacheEntryWithChecksum<T> : CacheEntry<T>
        {
            public string Checksum { get; private set; } = string.Empty;

            public CacheEntryWithChecksum()
            {
                UpdateChecksum();
            }

            private void UpdateChecksum()
            {
                var data = JsonSerializer.Serialize(new
                {
                    Value,
                    ExpirationTime,
                    CodeHash
                });
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                Checksum = Convert.ToBase64String(hash);
            }

            public bool VerifyChecksum()
            {
                var currentChecksum = Checksum;
                UpdateChecksum();
                return currentChecksum == Checksum;
            }
        }

        private class WALEntry
        {
            public string Key { get; set; } = string.Empty;
            public WALOperation Operation { get; set; }
            public string Data { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        private enum WALOperation
        {
            Set,
            Delete
        }
    }
}