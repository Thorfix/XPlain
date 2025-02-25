using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class CacheAccessStats
    {
        public long AccessCount { get; set; }
        public long PreWarmCount { get; set; }
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }
    public class FileBasedCacheProvider : ICacheProvider, ICacheEventListener
    {
        private ICacheEvictionPolicy _evictionPolicy;
        internal CircuitBreaker CircuitBreaker => _circuitBreaker;
        internal ICacheEvictionPolicy EvictionPolicy => _evictionPolicy;
        internal IEncryptionProvider EncryptionProvider { get; }
        
        private readonly CircuitBreaker _circuitBreaker;
        private readonly MLPredictionService _mlPredictionService;
        private readonly MetricsCollectionService _metricsService;
        private Dictionary<string, double> _resourceLimits;
        private readonly object _resourceLock = new object();
        private readonly Dictionary<string, CacheEntry> _cache;
        private readonly Dictionary<string, CacheAccessStats> _accessStats;
        
        // Metrics counters
        private long _hits;
        private long _misses;
        
        // Compression-related fields
        private readonly bool _compressionEnabled;
        private readonly CompressionAlgorithm _compressionAlgorithm;
        private readonly CompressionLevel _compressionLevel;
        private readonly int _minSizeForCompression;
        private readonly bool _adaptiveCompression;
        private readonly Dictionary<string, CompressionMetrics> _compressionStats;
        
        // Settings from configuration
        private readonly CacheSettings _cacheSettings;

        // IEncryptionProvider is not necessary for basic functionality
        public List<MaintenanceLogEntry> MaintenanceLogs { get; }

        public FileBasedCacheProvider(
            IOptions<CacheSettings> cacheSettings = null,
            MetricsCollectionService metricsService = null,
            MLPredictionService mlPredictionService = null,
            IEncryptionProvider encryptionProvider = null)
        {
            _cacheSettings = cacheSettings?.Value ?? new CacheSettings();
            
            _evictionPolicy = new DummyEvictionPolicy();
            _mlPredictionService = mlPredictionService;
            _metricsService = metricsService;
            _circuitBreaker = new CircuitBreaker(3, TimeSpan.FromMinutes(5));
            EncryptionProvider = encryptionProvider ?? new EncryptionProvider(cacheSettings);
            MaintenanceLogs = new List<MaintenanceLogEntry>();
            _cache = new Dictionary<string, CacheEntry>();
            _accessStats = new Dictionary<string, CacheAccessStats>();
            _compressionStats = new Dictionary<string, CompressionMetrics>();
            _resourceLimits = new Dictionary<string, double>
            {
                ["memory"] = 100,  // Default 100% of base allocation
                ["cpu"] = 100,     // Default 100% of base allocation
                ["storage"] = 100  // Default 100% of base allocation
            };
            
            // Initialize compression settings
            _compressionEnabled = _cacheSettings.CompressionEnabled;
            _compressionAlgorithm = _cacheSettings.CompressionAlgorithm;
            _compressionLevel = _cacheSettings.CompressionLevel;
            _minSizeForCompression = _cacheSettings.MinSizeForCompressionBytes;
            _adaptiveCompression = _cacheSettings.AdaptiveCompression;
            
            // Initialize compression metrics
            _compressionStats["GZip"] = new CompressionMetrics();
            _compressionStats["Brotli"] = new CompressionMetrics();
            _compressionStats["None"] = new CompressionMetrics();
            
            // Register this instance with the metrics service if provided
            if (_metricsService != null)
            {
                _ = AddEventListener(_metricsService);
            }
            
            MaintenanceLogs.Add(new MaintenanceLogEntry
            {
                Operation = "Initialization",
                Status = "Success",
                Metadata = new Dictionary<string, object>
                {
                    ["compression_enabled"] = _compressionEnabled,
                    ["compression_algorithm"] = _compressionAlgorithm.ToString(),
                    ["compression_level"] = _compressionLevel.ToString(),
                    ["adaptive_compression"] = _adaptiveCompression
                }
            });
        }

        // Simple dummy eviction policy implementation for basic functionality
        private class DummyEvictionPolicy : ICacheEvictionPolicy
        {
            public double CurrentEvictionThreshold { get; private set; } = 0.9;
            public double CurrentPressureThreshold { get; private set; } = 0.7;

            public Task<bool> UpdateEvictionThreshold(double threshold)
            {
                CurrentEvictionThreshold = threshold;
                return Task.FromResult(true);
            }

            public Task<bool> UpdatePressureThreshold(double threshold)
            {
                CurrentPressureThreshold = threshold;
                return Task.FromResult(true);
            }

            public Task<bool> ForceEviction(long bytesToFree)
            {
                return Task.FromResult(true);
            }

            public Dictionary<string, int> GetEvictionStats()
            {
                return new Dictionary<string, int>();
            }

            public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
            {
                return Enumerable.Empty<CacheEvictionEvent>();
            }
        }

        // Small maintenance log entry class
        public class MaintenanceLogEntry
        {
            public string Operation { get; set; }
            public string Status { get; set; }
            public Dictionary<string, object> Metadata { get; set; } = new();
        }

        // Using the CacheEvictionEvent class from ICacheEvictionPolicy

        public async Task<bool> UpdateResourceAllocation(Dictionary<string, double> newLimits)
        {
            try
            {
                lock (_resourceLock)
                {
                    foreach (var (resource, limit) in newLimits)
                    {
                        if (_resourceLimits.ContainsKey(resource))
                        {
                            var oldLimit = _resourceLimits[resource];
                            _resourceLimits[resource] = limit;

                            // Apply the new resource limits
                            ApplyResourceLimit(resource, limit);
                            
                            MaintenanceLogs.Add(new MaintenanceLogEntry
                            {
                                Operation = "ResourceAllocation",
                                Status = "Success",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["resource"] = resource,
                                    ["old_limit"] = oldLimit,
                                    ["new_limit"] = limit
                                }
                            });
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "ResourceAllocation",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }
                });
                return false;
            }
        }

        public async Task<List<string>> GetMostFrequentKeys(int count)
        {
            return _accessStats
                .OrderByDescending(x => x.Value.AccessCount)
                .Take(count)
                .Select(x => x.Key)
                .ToList();
        }

        public async Task<bool> IsKeyFresh(string key)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    stopwatch.Stop();
                    if (_metricsService != null)
                    {
                        await _metricsService.RecordQueryMetrics(key, stopwatch.ElapsedMilliseconds, !entry.IsExpired);
                    }
                    return !entry.IsExpired;
                }
                stopwatch.Stop();
                if (_metricsService != null)
                {
                    await _metricsService.RecordQueryMetrics(key, stopwatch.ElapsedMilliseconds, false);
                }
                return false;
            }
            catch
            {
                stopwatch.Stop();
                if (_metricsService != null)
                {
                    await _metricsService.RecordQueryMetrics(key, stopwatch.ElapsedMilliseconds, false);
                }
                throw;
            }
        }

        // Required ICacheProvider implementation
        public async Task<bool> PreWarmKey(string key, PreWarmPriority priority = PreWarmPriority.Medium)
        {
            try
            {
                // Mark as pre-warmed in access stats
                if (!_accessStats.ContainsKey(key))
                {
                    _accessStats[key] = new CacheAccessStats
                    {
                        PreWarmCount = 1,
                        LastAccess = DateTime.UtcNow
                    };
                }
                else
                {
                    _accessStats[key].PreWarmCount++;
                }
                
                await NotifyPreWarmListeners(key, true);
                await RefreshKey(key);
                return true;
            }
            catch (Exception)
            {
                await NotifyPreWarmListeners(key, false);
                return false;
            }
        }

        public async Task<T> GetAsync<T>(string key)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool isHit = false;
            
            try
            {
                if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
                {
                    isHit = true;
                    
                    // If the entry is compressed, decompress it first
                    if (entry.IsCompressed)
                    {
                        try
                        {
                            var compressedBytes = entry.Value as byte[] ?? 
                                                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry.Value);
                            
                            var decompressedBytes = DecompressData(compressedBytes, entry.CompressionAlgorithm);
                            
                            // Convert bytes to the requested type
                            if (typeof(T) == typeof(byte[]))
                            {
                                return (T)(object)decompressedBytes;
                            }
                            else
                            {
                                var decompressedValue = System.Text.Json.JsonSerializer.Deserialize<T>(decompressedBytes);
                                return decompressedValue;
                            }
                        }
                        catch (Exception ex)
                        {
                            MaintenanceLogs.Add(new MaintenanceLogEntry
                            {
                                Operation = "GetAsync_Decompression",
                                Status = "Error",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["error"] = ex.Message,
                                    ["key"] = key,
                                    ["algorithm"] = entry.CompressionAlgorithm.ToString()
                                }
                            });
                            
                            // If we can't decompress, check if the value can be directly cast to the requested type
                            if (entry.Value is T typedValue)
                            {
                                return typedValue;
                            }
                            
                            return default;
                        }
                    }
                    
                    // Handle non-compressed entry
                    if (entry.Value is T typedValue)
                    {
                        return typedValue;
                    }
                    
                    try
                    {
                        // Try to convert using JSON serialization
                        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry.Value);
                        return System.Text.Json.JsonSerializer.Deserialize<T>(bytes);
                    }
                    catch
                    {
                        // Last resort: try direct cast
                        return (T)entry.Value;
                    }
                }
                
                // Cache miss
                return default;
            }
            finally
            {
                stopwatch.Stop();
                
                // Update metrics
                if (isHit)
                {
                    Interlocked.Increment(ref _hits);
                }
                else
                {
                    Interlocked.Increment(ref _misses);
                }
                
                // Record query metrics
                if (_metricsService != null)
                {
                    _ = _metricsService.RecordQueryMetrics(key, stopwatch.ElapsedMilliseconds, isHit);
                }
                
                // Notify listeners
                _ = NotifyListeners(key, stopwatch.ElapsedMilliseconds, isHit);
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Calculate expiration time
                var expiryTime = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(_cacheSettings.CacheExpirationHours));
                
                // Serialize the value to determine size and enable compression
                byte[] serializedValue;
                if (typeof(T) == typeof(byte[]))
                {
                    serializedValue = value as byte[];
                }
                else
                {
                    serializedValue = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
                }
                
                var originalSize = serializedValue?.Length ?? 0;
                
                // Determine if this entry should be compressed
                var shouldCompress = _compressionEnabled && originalSize >= _minSizeForCompression;
                
                // Determine optimal compression algorithm and level for this data
                var compressionAlgorithm = shouldCompress 
                    ? DetermineOptimalCompressionAlgorithm(originalSize, typeof(T).Name) 
                    : CompressionAlgorithm.None;
                
                var compressionLevel = DetermineOptimalCompressionLevel(originalSize, typeof(T).Name);
                
                // Apply compression if needed
                byte[] compressedValue = null;
                long compressedSize = 0;
                bool compressionSucceeded = false;
                
                if (shouldCompress && compressionAlgorithm != CompressionAlgorithm.None)
                {
                    compressedValue = CompressData(serializedValue, compressionAlgorithm, compressionLevel);
                    compressedSize = compressedValue.Length;
                    
                    // Only use compression if it actually reduces size
                    compressionSucceeded = compressedSize < originalSize;
                }
                
                // Create the cache entry
                _cache[key] = new CacheEntry
                {
                    // Store the actual data - either compressed bytes or original value
                    Value = compressionSucceeded ? compressedValue : value,
                    ExpirationTime = expiryTime,
                    Size = originalSize,
                    CompressedSize = compressionSucceeded ? compressedSize : originalSize,
                    IsCompressed = compressionSucceeded,
                    CompressionAlgorithm = compressionAlgorithm,
                    CacheFormatVersion = 2 // Version 2 adds compression support
                };
                
                // Log compression metrics
                if (compressionSucceeded)
                {
                    MaintenanceLogs.Add(new MaintenanceLogEntry
                    {
                        Operation = "Compression",
                        Status = "Success",
                        Metadata = new Dictionary<string, object>
                        {
                            ["key"] = key,
                            ["original_size"] = originalSize,
                            ["compressed_size"] = compressedSize,
                            ["compression_ratio"] = (double)compressedSize / originalSize,
                            ["algorithm"] = compressionAlgorithm.ToString(),
                            ["level"] = compressionLevel.ToString()
                        }
                    });
                }
                
                // Update access stats
                if (!_accessStats.ContainsKey(key))
                {
                    _accessStats[key] = new CacheAccessStats();
                }
                _accessStats[key].LastAccess = DateTime.UtcNow;
                
                // If this is an upgrade from an uncompressed entry
                if (_cache.TryGetValue(key, out var existingEntry) && 
                    _cacheSettings.UpgradeUncompressedEntries &&
                    IsLegacyCacheEntry(existingEntry))
                {
                    MaintenanceLogs.Add(new MaintenanceLogEntry
                    {
                        Operation = "UpgradeCacheEntry",
                        Status = "Success",
                        Metadata = new Dictionary<string, object>
                        {
                            ["key"] = key,
                            ["from_version"] = existingEntry.CacheFormatVersion,
                            ["to_version"] = 2,
                            ["compression_applied"] = compressionSucceeded
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "SetAsync",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["key"] = key
                    }
                });
                throw;
            }
            finally
            {
                stopwatch.Stop();
                
                // Notify listeners about this cache operation
                if (_metricsService != null)
                {
                    _ = _metricsService.RecordCacheWriteMetric(key, stopwatch.ElapsedMilliseconds);
                }
            }
        }

        public async Task RemoveAsync(string key)
        {
            _cache.Remove(key);
        }

        public async Task<bool> ExistsAsync(string key)
        {
            return _cache.ContainsKey(key) && !_cache[key].IsExpired;
        }

        public async Task WarmupCacheAsync(string[] questions, string codeContext)
        {
            // Simple implementation that just marks these questions as pre-warmed
            foreach (var question in questions)
            {
                if (!_accessStats.ContainsKey(question))
                {
                    _accessStats[question] = new CacheAccessStats
                    {
                        PreWarmCount = 1,
                        LastAccess = DateTime.UtcNow
                    };
                }
                else
                {
                    _accessStats[question].PreWarmCount++;
                }
            }
        }

        public async Task InvalidateOnCodeChangeAsync(string codeHash)
        {
            // Simple implementation that just logs the code hash change
            MaintenanceLogs.Add(new MaintenanceLogEntry
            {
                Operation = "CodeHashChange",
                Status = "Success",
                Metadata = new Dictionary<string, object>
                {
                    ["codeHash"] = codeHash
                }
            });
        }

        public async Task<string> GeneratePerformanceChartAsync(OutputFormat format)
        {
            // Simple implementation that returns a placeholder chart
            return "Performance Chart (placeholder)";
        }

        public async Task<List<string>> GetCacheWarmingRecommendationsAsync()
        {
            // Simple implementation that returns placeholder recommendations
            return new List<string>
            {
                "Consider warming up frequently accessed queries",
                "Optimize cache size based on memory constraints"
            };
        }

        public async Task LogQueryStatsAsync(string queryType, string query, double responseTime, bool hit)
        {
            // Simple implementation that just logs the query stats
            if (!_accessStats.ContainsKey(query))
            {
                _accessStats[query] = new CacheAccessStats
                {
                    AccessCount = 1,
                    LastAccess = DateTime.UtcNow
                };
            }
            else
            {
                _accessStats[query].AccessCount++;
                _accessStats[query].LastAccess = DateTime.UtcNow;
            }
        }

        public CacheStats GetCacheStats()
        {
            // Calculate compressed storage usage
            var compressedStorageBytes = _cache.Sum(c => c.Value.IsCompressed ? c.Value.CompressedSize : c.Value.Size);
            var originalStorageBytes = _cache.Sum(c => c.Value.Size);
            
            // Count how many items are actually compressed
            var compressedItemCount = _cache.Count(c => c.Value.IsCompressed);
            
            // Create deep copy of compression stats to prevent modification while in use
            var compressionStatsCopy = new Dictionary<string, CompressionMetrics>();
            foreach (var (algorithm, stats) in _compressionStats)
            {
                compressionStatsCopy[algorithm] = new CompressionMetrics
                {
                    TotalItems = stats.TotalItems,
                    CompressedItems = stats.CompressedItems,
                    OriginalSizeBytes = stats.OriginalSizeBytes,
                    CompressedSizeBytes = stats.CompressedSizeBytes,
                    AverageCompressionTimeMs = stats.AverageCompressionTimeMs,
                    AverageDecompressionTimeMs = stats.AverageDecompressionTimeMs
                };
            }
            
            // Return enhanced cache stats with compression information
            return new CacheStats
            {
                Hits = Interlocked.Read(ref _hits),
                Misses = Interlocked.Read(ref _misses),
                CachedItemCount = _cache.Count,
                StorageUsageBytes = originalStorageBytes,
                CompressedStorageUsageBytes = compressedStorageBytes,
                QueryTypeStats = new Dictionary<string, long>(),
                AverageResponseTimes = new Dictionary<string, double>(),
                PerformanceByQueryType = new Dictionary<string, CachePerformanceMetrics>(),
                InvalidationHistory = new List<CacheInvalidationEvent>(),
                InvalidationCount = 0,
                TopQueries = _accessStats.OrderByDescending(a => a.Value.AccessCount)
                    .Take(5)
                    .ToDictionary(a => a.Key, a => (int)a.Value.AccessCount),
                LastStatsUpdate = DateTime.UtcNow,
                CompressionStats = compressionStatsCopy,
                EncryptionStatus = new EncryptionStatus
                {
                    Enabled = _cacheSettings.EncryptionEnabled,
                    Algorithm = _cacheSettings.EncryptionAlgorithm,
                    KeySize = _cacheSettings.EncryptionKeySize,
                    EncryptedFileCount = _cacheSettings.EncryptionEnabled ? _cache.Count : 0
                }
            };
        }

        private readonly List<ICacheEventListener> _eventListeners = new List<ICacheEventListener>();
        
        public async Task AddEventListener(ICacheEventListener listener)
        {
            if (listener == null) return;
            
            lock (_eventListeners)
            {
                if (!_eventListeners.Contains(listener))
                {
                    _eventListeners.Add(listener);
                }
            }
        }

        public async Task RemoveEventListener(ICacheEventListener listener)
        {
            if (listener == null) return;
            
            lock (_eventListeners)
            {
                _eventListeners.Remove(listener);
            }
        }
        
        private async Task NotifyListeners(string key, double responseTime, bool isHit)
        {
            var listeners = new List<ICacheEventListener>();
            
            lock (_eventListeners)
            {
                listeners.AddRange(_eventListeners);
            }
            
            foreach (var listener in listeners)
            {
                try
                {
                    await listener.OnCacheAccess(key, responseTime, isHit);
                }
                catch (Exception)
                {
                    // Suppress exceptions from listeners to prevent cache operations from failing
                }
            }
        }
        
        private async Task NotifyEvictionListeners(string key)
        {
            var listeners = new List<ICacheEventListener>();
            
            lock (_eventListeners)
            {
                listeners.AddRange(_eventListeners);
            }
            
            foreach (var listener in listeners)
            {
                try
                {
                    await listener.OnCacheEviction(key);
                }
                catch (Exception)
                {
                    // Suppress exceptions from listeners
                }
            }
        }

        private async Task NotifyPreWarmListeners(string key, bool success)
        {
            var listeners = new List<ICacheEventListener>();
            
            lock (_eventListeners)
            {
                listeners.AddRange(_eventListeners);
            }
            
            foreach (var listener in listeners)
            {
                try
                {
                    await listener.OnCachePreWarm(key, success);
                }
                catch (Exception)
                {
                    // Suppress exceptions from listeners
                }
            }
        }

        private async Task RefreshKey(string key)
        {
            if (_cache.ContainsKey(key))
            {
                var entry = _cache[key];
                if (entry.IsExpired)
                {
                    // Refresh the cache entry
                    await RefreshCacheEntry(key);
                }
                
                // Update access stats
                if (!_accessStats.ContainsKey(key))
                {
                    _accessStats[key] = new CacheAccessStats();
                }
                _accessStats[key].PreWarmCount++;
            }
        }

        private async Task RefreshCacheEntry(string key)
        {
            // Implementation to refresh a cache entry
            // This would typically involve fetching fresh data from the original source
            throw new NotImplementedException();
        }

        private void ApplyResourceLimit(string resource, double limit)
        {
            switch (resource.ToLower())
            {
                case "memory":
                    AdjustMemoryLimit(limit);
                    break;
                case "cpu":
                    AdjustCpuLimit(limit);
                    break;
                case "storage":
                    AdjustStorageLimit(limit);
                    break;
            }
        }



        private Dictionary<string, DateTime> AnalyzeQueryTimings()
        {
            var patterns = new Dictionary<string, DateTime>();
            var now = DateTime.UtcNow;

            foreach (var (key, stats) in _accessStats)
            {
                // For each query, determine the optimal time to pre-warm based on access patterns
                var nextAccessTime = PredictNextAccessTime(key, stats);
                
                // Schedule pre-warming slightly before predicted access time
                patterns[key] = nextAccessTime.AddMinutes(-5);
            }

            return patterns;
        }

        private DateTime PredictNextAccessTime(string key, CacheAccessStats stats)
        {
            // This would normally use more sophisticated prediction logic
            // For now, use a simple offset based on the hash of the key
            var hourOffset = Math.Abs(key.GetHashCode() % 24);
            return DateTime.UtcNow.Date.AddHours(hourOffset);
        }

        private TimeSpan CalculateOptimalInterval(CacheStats stats)
        {
            // Calculate average time between cache invalidations
            var avgInvalidationInterval = stats.InvalidationHistory.Count > 1
                ? stats.InvalidationHistory.Zip(
                    stats.InvalidationHistory.Skip(1),
                    (a, b) => b.Time - a.Time
                ).Average(ts => ts.TotalMinutes)
                : 15;

            // Use half the average invalidation interval, but keep within reasonable bounds
            return TimeSpan.FromMinutes(Math.Max(5, Math.Min(avgInvalidationInterval * 0.5, 60)));
        }
        
        public async Task<List<CacheAnalyticsEntry>> GetAnalyticsHistoryAsync(DateTime startTime)
        {
            // Simple implementation - just return some mock data
            var result = new List<CacheAnalyticsEntry>();
            
            // Generate some mock entries spanning from startTime to now
            var now = DateTime.UtcNow;
            var interval = (now - startTime).TotalHours / 10; // 10 data points
            
            for (int i = 0; i < 10; i++)
            {
                var timestamp = startTime.AddHours(interval * i);
                var hitRatio = 0.5 + (0.4 * Math.Sin(i * 0.5)); // Between 0.1 and 0.9
                var stats = new CacheStats
                {
                    HitRatio = hitRatio,
                    Hits = (long)(100 * hitRatio),
                    Misses = (long)(100 * (1 - hitRatio)),
                    CachedItemCount = 50 + (i * 5)
                };
                
                result.Add(new CacheAnalyticsEntry
                {
                    Timestamp = timestamp,
                    Stats = stats,
                    MemoryUsageMB = 20 + (i * 2),
                    QueryCount = 50 + (i * 5)
                });
            }
            
            return result;
        }
        
        public class CacheAnalyticsEntry
        {
            public DateTime Timestamp { get; set; }
            public CacheStats Stats { get; set; }
            public double MemoryUsageMB { get; set; }
            public int QueryCount { get; set; }
        }

        private double CalculateResourceThreshold()
        {
            // Use the most constrained resource as the threshold
            return Math.Min(
                _resourceLimits.GetValueOrDefault("memory", 100),
                _resourceLimits.GetValueOrDefault("cpu", 100)
            ) * 0.8; // 80% of the most constrained resource
        }

        public async Task UpdateEvictionPolicy(EvictionStrategy strategy)
        {
            // Create new policy instance based on strategy
            ICacheEvictionPolicy newPolicy = strategy switch
            {
                EvictionStrategy.LRU => new LRUEvictionPolicy(),
                EvictionStrategy.HitRateWeighted => new HitRateWeightedEvictionPolicy(_accessStats),
                EvictionStrategy.SizeWeighted => new SizeWeightedEvictionPolicy(),
                EvictionStrategy.Adaptive => new AdaptiveCacheEvictionPolicy(_accessStats),
                _ => throw new ArgumentException("Unknown eviction strategy")
            };

            // Transfer current thresholds to new policy
            await newPolicy.UpdateEvictionThreshold(_evictionPolicy.CurrentEvictionThreshold);
            await newPolicy.UpdatePressureThreshold(_evictionPolicy.CurrentPressureThreshold);

            // Switch to new policy
            _evictionPolicy = newPolicy;

            MaintenanceLogs.Add(new MaintenanceLogEntry
            {
                Operation = "EvictionPolicyChange",
                Status = "Success",
                Metadata = new Dictionary<string, object>
                {
                    ["newStrategy"] = strategy.ToString(),
                    ["evictionThreshold"] = _evictionPolicy.CurrentEvictionThreshold,
                    ["pressureThreshold"] = _evictionPolicy.CurrentPressureThreshold
                }
            });
        }

        private void AdjustMemoryLimit(double limit)
        {
            var baseMemory = 1024L * 1024L * 1024L; // 1GB base
            var newLimit = (long)(baseMemory * (limit / 100.0));
            
            // Adjust cache size and eviction thresholds based on new memory limit
            _evictionPolicy.UpdateEvictionThreshold(0.9 * (limit / 100.0));
            
            // Force eviction if above new limit
            if (GetCurrentMemoryUsage() > newLimit)
            {
                _evictionPolicy.ForceEviction(GetCurrentMemoryUsage() - newLimit);
            }

            // Consider switching to size-weighted eviction if memory pressure is high
            if (limit > 90)
            {
                UpdateEvictionPolicy(EvictionStrategy.SizeWeighted).Wait();
            }
        }

        private void AdjustCpuLimit(double limit)
        {
            // Adjust thread pool and parallel operation settings based on CPU limit
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            var newWorkerThreads = (int)(workerThreads * (limit / 100.0));
            var newCompletionPortThreads = (int)(completionPortThreads * (limit / 100.0));
            ThreadPool.SetMinThreads(newWorkerThreads, newCompletionPortThreads);
        }

        private void AdjustStorageLimit(double limit)
        {
            var baseStorage = 10L * 1024L * 1024L * 1024L; // 10GB base
            var newLimit = (long)(baseStorage * (limit / 100.0));
            
            // Adjust storage usage if needed
            if (GetCurrentStorageUsage() > newLimit)
            {
                _evictionPolicy.ForceEviction(GetCurrentStorageUsage() - newLimit);
            }
        }

        private long GetCurrentMemoryUsage()
        {
            return GC.GetTotalMemory(false);
        }

        private long GetCurrentStorageUsage()
        {
            // Calculate total size of cache entries, using compressed size when available
            return _cache.Sum(entry => entry.Value.IsCompressed ? entry.Value.CompressedSize : entry.Value.Size);
        }
        
        // Methods to handle cache format version compatibility
        private bool IsLegacyCacheEntry(CacheEntry entry)
        {
            return entry.CacheFormatVersion < 2;
        }
        
        private async Task MigrateLegacyCacheEntries()
        {
            int migratedCount = 0;
            
            foreach (var key in _cache.Keys.ToList())
            {
                var entry = _cache[key];
                if (IsLegacyCacheEntry(entry))
                {
                    try
                    {
                        // Re-serialize the entry to apply compression
                        await SetAsync(key, entry.Value, entry.ExpirationTime - DateTime.UtcNow);
                        migratedCount++;
                    }
                    catch (Exception ex)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "LegacyCacheMigration",
                            Status = "Error",
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = ex.Message,
                                ["key"] = key
                            }
                        });
                    }
                }
            }
            
            if (migratedCount > 0)
            {
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "LegacyCacheMigration",
                    Status = "Success",
                    Metadata = new Dictionary<string, object>
                    {
                        ["migrated_entries"] = migratedCount
                    }
                });
            }
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return _evictionPolicy.GetEvictionStats();
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _evictionPolicy.GetRecentEvictions(count);
        }
        
        // ICacheEventListener implementation
        public Task OnCacheAccess(string key, double responseTime, bool isHit)
        {
            if (!_accessStats.ContainsKey(key))
            {
                _accessStats[key] = new CacheAccessStats();
            }
            
            _accessStats[key].AccessCount++;
            _accessStats[key].LastAccess = DateTime.UtcNow;
            
            return Task.CompletedTask;
        }

        public Task OnCacheEviction(string key)
        {
            // Track eviction statistics
            if (_cache.ContainsKey(key))
            {
                _cache.Remove(key);
            }
            
            return Task.CompletedTask;
        }

        public Task OnCachePreWarm(string key, bool success)
        {
            if (!_accessStats.ContainsKey(key))
            {
                _accessStats[key] = new CacheAccessStats();
            }
            
            _accessStats[key].PreWarmCount++;
            
            return Task.CompletedTask;
        }

        public async Task<Dictionary<string, PreWarmMetrics>> GetPreWarmCandidatesAsync()
        {
            var result = new Dictionary<string, PreWarmMetrics>();
            var stats = GetCacheStats();

            foreach (var (key, accessStat) in _accessStats)
            {
                var metrics = new PreWarmMetrics
                {
                    UsageFrequency = accessStat.AccessCount,
                    LastAccessed = accessStat.LastAccess,
                    AverageResponseTime = stats.AverageResponseTimes.GetValueOrDefault(key, 0),
                    PerformanceImpact = stats.PerformanceByQueryType
                        .GetValueOrDefault(key, new CachePerformanceMetrics())?.PerformanceGain ?? 0,
                    ResourceCost = _cache.ContainsKey(key) ? _cache[key].Size : 0,
                    PredictedValue = await _mlPredictionService.PredictQueryValueAsync(key),
                    RecommendedPriority = DeterminePreWarmPriority(
                        accessStat.AccessCount,
                        accessStat.LastAccess,
                        stats.AverageResponseTimes.GetValueOrDefault(key, 0))
                };

                result[key] = metrics;
            }

            return result;
        }

        public async Task<bool> PreWarmBatchAsync(IEnumerable<string> keys, PreWarmPriority priority)
        {
            try
            {
                int successCount = 0;
                foreach (var key in keys)
                {
                    if (await PreWarmKey(key, priority))
                        successCount++;
                }

                // Consider batch successful if at least 70% of keys were pre-warmed
                return successCount >= keys.Count() * 0.7;
            }
            catch (Exception ex)
            {
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "PreWarmBatch",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["priority"] = priority,
                        ["keyCount"] = keys.Count()
                    }
                });
                return false;
            }
        }

        public async Task<PreWarmingStrategy> OptimizePreWarmingStrategyAsync()
        {
            var candidates = await GetPreWarmCandidatesAsync();
            var stats = GetCacheStats();
            
            // Calculate optimal batch size based on system resources
            int batchSize = CalculateOptimalBatchSize(stats);
            
            // Determine interval based on access patterns
            var interval = CalculateOptimalInterval(stats);
            
            // Get ML predictions for optimal timing
            var timingPredictions = await _mlPredictionService.PredictOptimalTimingsAsync(
                candidates.Keys.ToList()
            );

            return new PreWarmingStrategy
            {
                KeyPriorities = candidates.ToDictionary(
                    c => c.Key,
                    c => c.Value.RecommendedPriority
                ),
                BatchSize = batchSize,
                PreWarmInterval = interval,
                ResourceThreshold = CalculateResourceThreshold(),
                OptimalTimings = timingPredictions
            };
        }

        private PreWarmPriority DeterminePreWarmPriority(
            long accessCount,
            DateTime lastAccess,
            double averageResponseTime)
        {
            var recency = DateTime.UtcNow - lastAccess;
            var frequencyScore = accessCount / (recency.TotalHours + 1);
            var performanceScore = averageResponseTime / 1000.0; // Convert to seconds

            var score = frequencyScore * performanceScore;

            return score switch
            {
                > 100 => PreWarmPriority.Critical,
                > 50 => PreWarmPriority.High,
                > 20 => PreWarmPriority.Medium,
                _ => PreWarmPriority.Low
            };
        }

        private int CalculateOptimalBatchSize(CacheStats stats)
        {
            var availableMemory = GetAvailableMemory();
            var averageEntrySize = stats.StorageUsageBytes / Math.Max(1, stats.CachedItemCount);
            
            // Calculate how many items we can safely pre-warm at once
            var maxBatchSize = (int)(availableMemory * 0.1 / averageEntrySize); // Use max 10% of available memory
            
            return Math.Max(1, Math.Min(maxBatchSize, 100)); // Cap between 1 and 100
        }

        private TimeSpan CalculateOptimalInterval(CacheStats stats)
        {
            var averageInvalidationInterval = stats.InvalidationHistory.Count > 1
                ? stats.InvalidationHistory.Zip(
                    stats.InvalidationHistory.Skip(1),
                    (a, b) => b.Time - a.Time
                ).Average(ts => ts.TotalMinutes)
                : 15;

            return TimeSpan.FromMinutes(Math.Max(5, Math.Min(averageInvalidationInterval * 0.5, 60)));
        }

        private double CalculateResourceThreshold()
        {
            return Math.Min(
                _resourceLimits.GetValueOrDefault("memory", 100),
                _resourceLimits.GetValueOrDefault("cpu", 100)
            ) * 0.8; // 80% of the most constrained resource
        }

        private long GetAvailableMemory()
        {
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var used = GC.GetTotalMemory(false);
            return total - used;
        }
        
        // Compression-related methods
        private byte[] CompressData(byte[] data, CompressionAlgorithm algorithm, CompressionLevel level)
        {
            if (data == null || data.Length == 0)
                return data;
                
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                using var outputStream = new MemoryStream();
                
                switch (algorithm)
                {
                    case CompressionAlgorithm.GZip:
                        using (var gzipStream = new GZipStream(outputStream, level))
                        {
                            gzipStream.Write(data, 0, data.Length);
                            gzipStream.Flush();
                        }
                        break;
                        
                    case CompressionAlgorithm.Brotli:
                        using (var brotliStream = new BrotliStream(outputStream, level))
                        {
                            brotliStream.Write(data, 0, data.Length);
                            brotliStream.Flush();
                        }
                        break;
                        
                    case CompressionAlgorithm.None:
                    default:
                        return data;
                }
                
                var result = outputStream.ToArray();
                
                // Update compression stats
                stopwatch.Stop();
                UpdateCompressionStats(algorithm.ToString(), data.Length, result.Length, stopwatch.ElapsedMilliseconds, 0);
                
                // Only return compressed data if it's actually smaller
                if (result.Length < data.Length)
                {
                    return result;
                }
                
                // If compression didn't reduce size, return original data
                UpdateCompressionStats(algorithm.ToString(), data.Length, data.Length, stopwatch.ElapsedMilliseconds, 0, false);
                return data;
            }
            catch (Exception ex)
            {
                // Log error and return original data if compression fails
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "Compression",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["algorithm"] = algorithm.ToString(),
                        ["data_size"] = data.Length
                    }
                });
                return data;
            }
        }
        
        private byte[] DecompressData(byte[] data, CompressionAlgorithm algorithm)
        {
            if (data == null || data.Length == 0)
                return data;
                
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // If the algorithm is None, return the original data
                if (algorithm == CompressionAlgorithm.None)
                    return data;
                
                using var inputStream = new MemoryStream(data);
                using var outputStream = new MemoryStream();
                
                switch (algorithm)
                {
                    case CompressionAlgorithm.GZip:
                        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                        {
                            gzipStream.CopyTo(outputStream);
                        }
                        break;
                        
                    case CompressionAlgorithm.Brotli:
                        using (var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress))
                        {
                            brotliStream.CopyTo(outputStream);
                        }
                        break;
                        
                    default:
                        return data;
                }
                
                var result = outputStream.ToArray();
                
                // Update decompression stats
                stopwatch.Stop();
                UpdateCompressionStats(algorithm.ToString(), result.Length, data.Length, 0, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                // Log error and return original data if decompression fails
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "Decompression",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["algorithm"] = algorithm.ToString(),
                        ["data_size"] = data.Length
                    }
                });
                return data;
            }
        }
        
        private void UpdateCompressionStats(string algorithm, long originalSize, long compressedSize, 
                                           double compressionTimeMs, double decompressionTimeMs, 
                                           bool wasCompressed = true)
        {
            if (!_compressionStats.ContainsKey(algorithm))
            {
                _compressionStats[algorithm] = new CompressionMetrics();
            }
            
            var stats = _compressionStats[algorithm];
            
            if (compressionTimeMs > 0)
            {
                // Compression stats update
                stats.TotalItems++;
                if (wasCompressed && compressedSize < originalSize)
                {
                    stats.CompressedItems++;
                    stats.OriginalSizeBytes += originalSize;
                    stats.CompressedSizeBytes += compressedSize;
                }
                else
                {
                    // Track original size for both compressed and uncompressed items
                    stats.OriginalSizeBytes += originalSize;
                    stats.CompressedSizeBytes += originalSize; // Use original size since compression was skipped or ineffective
                }
                
                // Update average compression time (weighted moving average)
                if (stats.TotalItems > 1)
                {
                    stats.AverageCompressionTimeMs = (stats.AverageCompressionTimeMs * (stats.TotalItems - 1) + compressionTimeMs) / stats.TotalItems;
                }
                else
                {
                    stats.AverageCompressionTimeMs = compressionTimeMs;
                }
            }
            
            if (decompressionTimeMs > 0)
            {
                // Update average decompression time (weighted moving average)
                if (stats.TotalItems > 0)
                {
                    stats.AverageDecompressionTimeMs = (stats.AverageDecompressionTimeMs * (stats.TotalItems - 1) + decompressionTimeMs) / stats.TotalItems;
                }
                else
                {
                    stats.AverageDecompressionTimeMs = decompressionTimeMs;
                }
            }
        }
        
        private CompressionAlgorithm DetermineOptimalCompressionAlgorithm(int dataSize, string dataType)
        {
            if (!_adaptiveCompression)
            {
                return _compressionAlgorithm;
            }
            
            // For very small data, don't compress
            if (dataSize < _minSizeForCompression)
            {
                return CompressionAlgorithm.None;
            }
            
            // Use content-based heuristics for optimal algorithm selection
            if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true || 
                dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Text-based data compresses well with either algorithm
                // Use Brotli for larger data (better compression but slower)
                // Use GZip for smaller data (faster but less compression)
                return dataSize > 100 * 1024 ? CompressionAlgorithm.Brotli : CompressionAlgorithm.GZip;
            }
            
            if (dataType?.Contains("image", StringComparison.OrdinalIgnoreCase) == true || 
                dataType?.Contains("video", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("zip", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("compressed", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Binary formats like images/videos are often already compressed, skip compression
                return CompressionAlgorithm.None;
            }
            
            // Check compression stats to make a data-driven decision
            if (_compressionStats.ContainsKey("GZip") && _compressionStats.ContainsKey("Brotli") &&
                _compressionStats["GZip"].TotalItems > 10 && _compressionStats["Brotli"].TotalItems > 10)
            {
                // We have enough data to make an informed decision
                var gzipEfficiency = _compressionStats["GZip"].CompressionRatio;
                var brotliEfficiency = _compressionStats["Brotli"].CompressionRatio;
                
                // Choose the algorithm with better compression ratio
                if (brotliEfficiency < gzipEfficiency * 0.9) // If Brotli is at least 10% better
                {
                    return CompressionAlgorithm.Brotli;
                }
                else
                {
                    return CompressionAlgorithm.GZip; // Otherwise use GZip for better performance
                }
            }
            
            // If in doubt, for large data use Brotli (better compression)
            if (dataSize > 1024 * 1024) // > 1MB
            {
                return CompressionAlgorithm.Brotli;
            }
            
            // Default to GZip for balanced performance/compression ratio
            return CompressionAlgorithm.GZip;
        }
        
        private CompressionLevel DetermineOptimalCompressionLevel(int dataSize, string dataType)
        {
            if (!_adaptiveCompression)
            {
                return _compressionLevel;
            }
            
            // For extremely large data, always use fastest compression
            if (dataSize > 50 * 1024 * 1024) // 50MB
            {
                return CompressionLevel.Fastest;
            }
            
            // For very large data, use faster compression in most cases
            if (dataSize > 10 * 1024 * 1024) // 10MB
            {
                // For very compressible data types, it might still be worth using optimal compression
                if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return CompressionLevel.Optimal;
                }
                
                return CompressionLevel.Fastest;
            }
            
            // For medium data, use optimal approach for most cases
            if (dataSize > 1 * 1024 * 1024) // 1MB
            {
                // For binary data that might compress poorly, use fastest
                if (dataType?.Contains("binary", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return CompressionLevel.Fastest;
                }
                
                return CompressionLevel.Optimal;
            }
            
            // For smaller data, use maximum compression if it's a format that compresses well
            if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true)
            {
                return CompressionLevel.SmallestSize;
            }
            
            // Default to optimal for small to medium data of unknown type
            return CompressionLevel.Optimal;
        }

        private class CacheEntry
        {
            public object Value { get; set; }
            public DateTime ExpirationTime { get; set; }
            public long Size { get; set; }
            public long CompressedSize { get; set; }
            public bool IsCompressed { get; set; }
            public CompressionAlgorithm CompressionAlgorithm { get; set; }
            public int CacheFormatVersion { get; set; } = 2; // Version 2 adds compression support
            public bool IsExpired => DateTime.UtcNow > ExpirationTime;
            
            public double CompressionRatio => IsCompressed && Size > 0 ? (double)CompressedSize / Size : 1.0;
        }
    }
}