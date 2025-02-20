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
        private readonly ICacheMonitoringService _monitoringService;
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
        private readonly ICacheEvictionPolicy _evictionPolicy;
        private readonly ConcurrentDictionary<string, int> _queryTypeStats = new();
        private readonly ConcurrentDictionary<string, (int Count, double TotalTimeCached, double TotalTimeNonCached)> _queryResponseStats = new();
        private readonly ConcurrentDictionary<string, int> _queryFrequency = new();
        private readonly ConcurrentQueue<(DateTime Time, int ItemsInvalidated)> _invalidationHistory = new();
        private readonly string _statsFilePath;
        private readonly string _analyticsFilePath;
        private readonly Timer _analyticsTimer;
        private readonly Timer _maintenanceTimer;
        private readonly Timer _warmupTimer;
        private readonly Timer _keyRotationTimer;
        private readonly string _keyStorePath;
        private readonly byte[] _encryptionKey;
        private readonly object _encryptionLock = new object();
        private readonly object _maintenanceLock = new object();
        private readonly ConcurrentDictionary<string, DetailedCacheItemStats> _cacheItemStats = new();
        private DateTime _lastPolicySwitch = DateTime.MinValue;
        private readonly object _policySwitchLock = new object();

        private class DetailedCacheItemStats
        {
            public long Size { get; set; }
            public DateTime LastAccess { get; set; }
            public DateTime CreationTime { get; set; }
            public int AccessCount { get; set; }
            public double AverageResponseTime { get; set; }
            public int CacheHits { get; set; }
            public int CacheMisses { get; set; }
            public string QueryType { get; set; } = string.Empty;
            public double MemoryUsage { get; set; }
            public ConcurrentQueue<DateTime> AccessHistory { get; } = new();
            private const int MaxAccessHistorySize = 100;

            public void RecordAccess(DateTime accessTime, double responseTime, bool wasHit)
            {
                LastAccess = accessTime;
                AccessCount++;
                AccessHistory.Enqueue(accessTime);
                while (AccessHistory.Count > MaxAccessHistorySize)
                {
                    AccessHistory.TryDequeue(out _);
                }

                if (wasHit)
                {
                    CacheHits++;
                }
                else
                {
                    CacheMisses++;
                }

                // Update rolling average response time
                AverageResponseTime = ((AverageResponseTime * (AccessCount - 1)) + responseTime) / AccessCount;
            }

            public double GetAccessFrequency(TimeSpan window)
            {
                var cutoff = DateTime.UtcNow - window;
                return AccessHistory.Count(t => t >= cutoff) / window.TotalHours;
            }
        }

        public FileBasedCacheProvider(
            IOptions<CacheSettings> settings,
            ILLMProvider llmProvider,
            ICacheMonitoringService monitoringService,
            ICacheEvictionPolicy? evictionPolicy = null)
        {
            _settings = settings.Value;
            _evictionPolicy = evictionPolicy ?? CreateEvictionPolicy(_settings.EvictionPolicy);
            _llmProvider = llmProvider;
            _monitoringService = monitoringService;
            _cacheDirectory = _settings.CacheDirectory ?? Path.Combine(AppContext.BaseDirectory, "cache");
            _analyticsDirectory = Path.Combine(_cacheDirectory, "analytics");
            _walDirectory = Path.Combine(_cacheDirectory, "wal");
            _backupDirectory = Path.Combine(_cacheDirectory, "backups");
            _statsFilePath = Path.Combine(_cacheDirectory, "cache_stats.json");
            _analyticsFilePath = Path.Combine(_analyticsDirectory, "analytics.json");
            _keyStorePath = Path.Combine(_cacheDirectory, "keystore.dat");
            
            Directory.CreateDirectory(_cacheDirectory);
            Directory.CreateDirectory(_analyticsDirectory);
            Directory.CreateDirectory(_walDirectory);
            Directory.CreateDirectory(_backupDirectory);

            // Initialize encryption
            if (_settings.EncryptionEnabled)
            {
                _encryptionKey = InitializeEncryption();
            }
            
            // Initialize cache state
            InitializeCacheState();
            StartPeriodicBackup();
            LoadStatsFromDisk();

            // Set up periodic analytics logging (every hour)
            _analyticsTimer = new Timer(async _ => await LogAnalyticsAsync(), 
                null, TimeSpan.Zero, TimeSpan.FromHours(1));

            // Initialize maintenance timer
            _maintenanceTimer = new Timer(async _ => await PerformMaintenanceAsync(),
                null, TimeSpan.Zero, TimeSpan.FromMinutes(_settings.MaintenanceIntervalMinutes));

            // Initialize cache warmup timer
            if (_settings.EnableCacheWarming)
            {
                _warmupTimer = new Timer(async _ => await PerformCacheWarmupAsync(),
                    null, TimeSpan.Zero, TimeSpan.FromMinutes(_settings.WarmupIntervalMinutes));
            }

            // Initialize key rotation timer
            if (_settings.EnableKeyRotation && _settings.EncryptionEnabled)
            {
                _keyRotationTimer = new Timer(async _ => await RotateEncryptionKeyIfNeededAsync(),
                    null, TimeSpan.Zero, TimeSpan.FromHours(24));
            }

            // Load initial cache item stats
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
            {
                var fileInfo = new FileInfo(file);
                _cacheItemStats.TryAdd(
                    Path.GetFileNameWithoutExtension(file),
                    (fileInfo.Length, fileInfo.LastAccessTime)
                );
            }
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

            var startTime = DateTime.UtcNow;
            var fileInfo = new FileInfo(filePath);
            var cacheKey = Path.GetFileNameWithoutExtension(filePath);
            var queryType = GetQueryTypeFromKey(key);

            // Update detailed statistics
            _cacheItemStats.AddOrUpdate(
                cacheKey,
                _ => new DetailedCacheItemStats
                {
                    Size = fileInfo.Length,
                    CreationTime = fileInfo.CreationTimeUtc,
                    LastAccess = DateTime.UtcNow,
                    QueryType = queryType,
                    MemoryUsage = fileInfo.Length / (1024.0 * 1024.0)
                },
                (_, stats) =>
                {
                    stats.RecordAccess(DateTime.UtcNow, (DateTime.UtcNow - startTime).TotalMilliseconds, true);
                    stats.Size = fileInfo.Length;
                    stats.MemoryUsage = fileInfo.Length / (1024.0 * 1024.0);
                    stats.QueryType = queryType;
                    return stats;
                }
            );

            // Check if we should switch eviction policy
            await CheckAndSwitchPolicyAsync();

            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                
                // Try to decrypt if encryption is enabled
                if (_settings.EncryptionEnabled)
                {
                    try
                    {
                        fileData = DecryptData(fileData);
                    }
                    catch when (_settings.AllowUnencryptedLegacyFiles)
                    {
                        // If decryption fails and legacy files are allowed, try using the data as-is
                    }
                }

                var cacheEntry = await JsonSerializer.DeserializeAsync<CacheEntry<T>>(
                    new MemoryStream(fileData));

                if (cacheEntry == null || cacheEntry.IsExpired)
                {
                    await RemoveAsync(key);
                    IncrementMisses();
                    await _monitoringService.CreateAlertAsync(
                        "CacheExpiration",
                        $"Cache entry expired: {key}",
                        "Info");
                    return null;
                }

                IncrementHits();
                await _monitoringService.LogQueryStatsAsync(
                    GetQueryTypeFromKey(key),
                    key,
                    0, // response time not measured here
                    true);
                return cacheEntry.Value;
            }
            catch (Exception ex)
            {
                await RemoveAsync(key);
                IncrementMisses();
                await _monitoringService.CreateAlertAsync(
                    "CacheError",
                    $"Error reading cache entry: {key}, Error: {ex.Message}",
                    "Error");
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

            // Check cache size limits before adding new item
            await EnforceCacheLimitsAsync();

            var filePath = GetFilePath(key);
            var tempFilePath = filePath + ".tmp";
            var cacheEntry = new CacheEntryWithChecksum<T>
            {
                Value = value,
                ExpirationTime = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(_settings.CacheExpirationHours)),
                CodeHash = await CalculateCodeHashAsync()
            };

            // Write to temporary file first
            byte[] serializedData;
            using (var ms = new MemoryStream())
            {
                await JsonSerializer.SerializeAsync(ms, cacheEntry);
                serializedData = ms.ToArray();
            }

            // Encrypt if enabled
            if (_settings.EncryptionEnabled)
            {
                serializedData = EncryptData(serializedData);
            }

            await File.WriteAllBytesAsync(tempFilePath, serializedData);

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
                    EncryptionStatus = new CacheEncryptionStatus
                    {
                        Enabled = _settings.EncryptionEnabled,
                        Algorithm = _settings.EncryptionAlgorithm,
                        LastKeyRotation = File.GetLastWriteTimeUtc(_keyStorePath),
                        EncryptedFileCount = GetEncryptedFileCount()
                    },
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

        private int GetEncryptedFileCount()
        {
            if (!_settings.EncryptionEnabled) return 0;

            int count = 0;
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
            {
                try
                {
                    var data = File.ReadAllBytes(file);
                    DecryptData(data);
                    count++;
                }
                catch
                {
                    // Not an encrypted file
                }
            }
            return count;
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

        private byte[] InitializeEncryption()
        {
            if (string.IsNullOrEmpty(_settings.EncryptionKey))
            {
                // Generate new key if none provided
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.GenerateKey();
                var key = aes.Key;
                SaveEncryptionKey(key);
                return key;
            }
            else if (File.Exists(_keyStorePath))
            {
                // Load existing key
                return File.ReadAllBytes(_keyStorePath);
            }
            else
            {
                // Use provided key
                var key = Convert.FromBase64String(_settings.EncryptionKey);
                SaveEncryptionKey(key);
                return key;
            }
        }

        private void SaveEncryptionKey(byte[] key)
        {
            // In a production environment, this should use a secure key storage mechanism
            // like Windows DPAPI or a Hardware Security Module (HSM)
            File.WriteAllBytes(_keyStorePath, key);
        }

        private byte[] EncryptData(byte[] data)
        {
            if (!_settings.EncryptionEnabled) return data;

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            
            // Write IV first
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var bw = new BinaryWriter(cs))
            {
                bw.Write(data);
            }

            return ms.ToArray();
        }

        private byte[] DecryptData(byte[] encryptedData)
        {
            if (!_settings.EncryptionEnabled) return encryptedData;

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;

            // Read IV from the beginning of the encrypted data
            var iv = new byte[aes.BlockSize / 8];
            Array.Copy(encryptedData, iv, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream();
            
            using (var cs = new CryptoStream(
                new MemoryStream(encryptedData, iv.Length, encryptedData.Length - iv.Length),
                decryptor,
                CryptoStreamMode.Read))
            {
                cs.CopyTo(ms);
            }

            return ms.ToArray();
        }

        private async Task RotateEncryptionKeyAsync()
        {
            if (!_settings.EncryptionEnabled) return;

            lock (_encryptionLock)
            {
                // Generate new key
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.GenerateKey();
                var newKey = aes.Key;

                // Store old key for re-encryption
                var oldKey = _encryptionKey;

                // Re-encrypt all cache files with new key
                var files = Directory.GetFiles(_cacheDirectory, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var content = File.ReadAllBytes(file);
                        var decrypted = DecryptData(content);
                        
                        // Update encryption key and re-encrypt
                        _encryptionKey = newKey;
                        var reencrypted = EncryptData(decrypted);
                        
                        File.WriteAllBytes(file, reencrypted);
                    }
                    catch
                    {
                        // If decryption fails, file might be unencrypted legacy file
                        if (_settings.AllowUnencryptedLegacyFiles)
                        {
                            continue;
                        }
                        File.Delete(file);
                    }
                }

                // Save new key
                SaveEncryptionKey(newKey);
            }
        }

        private async Task PerformMaintenanceAsync()
        {
            if (!_settings.CacheEnabled) return;

            // Check if we're within maintenance window if restricted
            if (_settings.RestrictMaintenanceWindow)
            {
                var now = DateTime.Now.TimeOfDay;
                if (now < _settings.MaintenanceWindowStart || now > _settings.MaintenanceWindowEnd)
                {
                    return;
                }
            }

            lock (_maintenanceLock)
            {
                try
                {
                    // Calculate current cache size
                    var totalSize = GetCacheStorageUsage();
                    var sizeLimit = _settings.MaxCacheSizeBytes;
                    var cleanupThreshold = sizeLimit * _settings.CleanupThresholdPercent / 100;

                    if (totalSize > cleanupThreshold)
                    {
                        // Convert cache stats to format expected by eviction policy
                        var items = _cacheItemStats.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new CacheItemStats
                            {
                                Size = kvp.Value.Size,
                                LastAccess = kvp.Value.LastAccess,
                                CreationTime = File.GetCreationTimeUtc(GetFilePath(kvp.Key)),
                                AccessCount = _queryFrequency.GetValueOrDefault(kvp.Key, 0)
                            });

                        // Update policy with current stats
                        _evictionPolicy.UpdatePolicy(new CacheAccessStats
                        {
                            TotalHits = _cacheHits,
                            TotalMisses = _cacheMisses,
                            QueryTypeFrequency = new Dictionary<string, int>(_queryTypeStats),
                            AverageResponseTimes = new Dictionary<string, double>(
                                _queryResponseStats.ToDictionary(
                                    kvp => kvp.Key,
                                    kvp => (kvp.Value.TotalTimeCached + kvp.Value.TotalTimeNonCached) / kvp.Value.Count
                                ))
                        });

                        // Get items to evict
                        var targetSize = sizeLimit * 0.7; // Aim to reduce to 70% of limit
                        var itemsToEvict = _evictionPolicy.SelectItemsForEviction(items, targetSize);

                        // Remove selected items
                        foreach (var key in itemsToEvict)
                        {
                            var filePath = GetFilePath(key);
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                                _cacheItemStats.TryRemove(key, out _);
                            }
                        }

                        // Log policy metrics
                        var policyMetrics = _evictionPolicy.GetPolicyMetrics();
                        LogPolicyMetrics(policyMetrics);
                    }

                    // Enforce per-query-type quotas
                    if (_settings.QueryTypeQuotas.Any())
                    {
                        EnforceQueryTypeQuotas();
                    }

                    // Log maintenance results
                    LogMaintenanceResults(totalSize, GetCacheStorageUsage());
                }
                catch (Exception ex)
                {
                    // Log maintenance error
                    Console.WriteLine($"Cache maintenance error: {ex.Message}");
                }
            }
        }

        private async Task PerformCacheWarmupAsync()
        {
            if (!_settings.EnableCacheWarming) return;

            try
            {
                // Get top queries by frequency
                var topQueries = _queryFrequency
                    .Where(q => q.Value >= _settings.MinQueryFrequency)
                    .OrderByDescending(q => q.Value)
                    .Take(_settings.MaxWarmupQueries)
                    .Select(q => q.Key)
                    .ToList();

                // Get current code context
                var codeHash = await CalculateCodeHashAsync();

                foreach (var query in topQueries)
                {
                    var key = GetCacheKey(query, codeHash);
                    if (!await ExistsAsync(key))
                    {
                        try
                        {
                            var response = await _llmProvider.GetCompletionAsync(query);
                            await SetAsync(key, response);
                        }
                        catch
                        {
                            // Skip failed warmup attempts
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log warmup error
                Console.WriteLine($"Cache warmup error: {ex.Message}");
            }
        }

        private async Task RotateEncryptionKeyIfNeededAsync()
        {
            if (!_settings.EnableKeyRotation || !_settings.EncryptionEnabled) return;

            try
            {
                var keyAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(_keyStorePath);
                if (keyAge.TotalDays >= _settings.KeyRotationDays)
                {
                    await RotateEncryptionKeyAsync();
                }
            }
            catch (Exception ex)
            {
                // Log key rotation error
                Console.WriteLine($"Key rotation error: {ex.Message}");
            }
        }

        private async Task EnforceCacheLimitsAsync()
        {
            var totalSize = GetCacheStorageUsage();
            if (totalSize >= _settings.MaxCacheSizeBytes)
            {
                await _monitoringService.CreateAlertAsync(
                    "CacheSizeLimit",
                    $"Cache size exceeds limit: {totalSize / (1024.0 * 1024.0):F2}MB",
                    "Warning");
                await PerformMaintenanceAsync();
            }
        }

        private void EnforceQueryTypeQuotas()
        {
            foreach (var quota in _settings.QueryTypeQuotas)
            {
                var queryType = quota.Key;
                var maxSize = quota.Value;
                var currentSize = _cacheItemStats
                    .Where(x => GetQueryTypeFromKey(x.Key) == queryType)
                    .Sum(x => x.Value.Size);

                if (currentSize > maxSize)
                {
                    // Remove oldest items until under quota
                    var items = _cacheItemStats
                        .Where(x => GetQueryTypeFromKey(x.Key) == queryType)
                        .OrderBy(x => x.Value.LastAccess)
                        .ToList();

                    foreach (var item in items)
                    {
                        var filePath = GetFilePath(item.Key);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            currentSize -= item.Value.Size;
                            _cacheItemStats.TryRemove(item.Key, out _);
                        }

                        if (currentSize <= maxSize)
                        {
                            break;
                        }
                    }
                }
            }
        }

        private string GetQueryTypeFromKey(string key)
        {
            // Extract query type from cache key
            // Implementation depends on how query types are encoded in cache keys
            try
            {
                var decodedKey = Encoding.UTF8.GetString(Convert.FromBase64String(key));
                var parts = decodedKey.Split(':', 2);
                return parts[0];
            }
            catch
            {
                return "unknown";
            }
        }

        private ICacheEvictionPolicy CreateEvictionPolicy(EvictionPolicyType policyType)
        {
            return policyType switch
            {
                EvictionPolicyType.LRU => new LRUEvictionPolicy(),
                EvictionPolicyType.LFU => new LFUEvictionPolicy(),
                EvictionPolicyType.FIFO => new FIFOEvictionPolicy(),
                EvictionPolicyType.SizeWeighted => new SizeWeightedEvictionPolicy(),
                EvictionPolicyType.Hybrid => new HybridEvictionPolicy(),
                _ => new LRUEvictionPolicy() // Default to LRU
            };
        }

        private void LogMaintenanceResults(long originalSize, long newSize)
        {
            var stats = new
            {
                Timestamp = DateTime.UtcNow,
                OriginalSize = originalSize,
                NewSize = newSize,
                SpaceFreed = originalSize - newSize,
                RemainingItems = _cacheItemStats.Count,
                QueryTypeStats = _queryTypeStats,
                EvictionPolicy = _settings.EvictionPolicy.ToString(),
                PolicyMetrics = _evictionPolicy.GetPolicyMetrics()
            };

            var logFile = Path.Combine(_analyticsDirectory, 
                $"maintenance_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            
            File.WriteAllText(logFile, 
                JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void LogPolicyMetrics(Dictionary<string, double> metrics)
        {
            var logFile = Path.Combine(_analyticsDirectory, 
                $"policy_metrics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            
            File.WriteAllText(logFile, 
                JsonSerializer.Serialize(new
                {
                    Timestamp = DateTime.UtcNow,
                    PolicyType = _settings.EvictionPolicy.ToString(),
                    Metrics = metrics
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}