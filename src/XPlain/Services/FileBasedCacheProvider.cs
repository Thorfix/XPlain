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
        private readonly List<CompressionHistoryEntry> _compressionHistory = new List<CompressionHistoryEntry>();
        private readonly Dictionary<ContentType, CompressionMetrics> _compressionByContentType = new Dictionary<ContentType, CompressionMetrics>();
        private ContentType _lastContentType = ContentType.Unknown;
        
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
            
            // If upgrade of uncompressed entries is enabled, schedule a background task to handle it
            if (_cacheSettings.UpgradeUncompressedEntries)
            {
                Task.Run(async () => 
                {
                    try
                    {
                        await Task.Delay(5000); // Wait a bit for initialization to complete
                        await MigrateLegacyCacheEntries();
                    }
                    catch (Exception ex)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "AutomaticCacheMigration",
                            Status = "Error",
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = ex.Message,
                                ["stack_trace"] = ex.StackTrace
                            }
                        });
                    }
                });
            }
            
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
            bool wasCompressed = false;
            long originalSize = 0;
            long compressedSize = 0;
            CompressionAlgorithm algorithm = CompressionAlgorithm.None;
            var decompressStopwatch = new System.Diagnostics.Stopwatch();
            
            try
            {
                if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
                {
                    isHit = true;
                    wasCompressed = entry.IsCompressed;
                    originalSize = entry.Size;
                    compressedSize = entry.CompressedSize;
                    algorithm = entry.CompressionAlgorithm;
                    
                    // If the entry is compressed, decompress it first
                    if (entry.IsCompressed)
                    {
                        try
                        {
                            // Ensure we have the compressed bytes
                            var compressedBytes = entry.Value as byte[];
                            if (compressedBytes == null)
                            {
                                // This shouldn't happen with properly compressed entries, but as a fallback
                                try
                                {
                                    compressedBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry.Value);
                                }
                                catch (Exception ex)
                                {
                                    MaintenanceLogs.Add(new MaintenanceLogEntry
                                    {
                                        Operation = "GetAsync_SerializationOfCompressedValue",
                                        Status = "Error",
                                        Metadata = new Dictionary<string, object>
                                        {
                                            ["error"] = ex.Message,
                                            ["key"] = key,
                                            ["value_type"] = entry.Value?.GetType().Name ?? "null"
                                        }
                                    });
                                    
                                    // Last resort: try direct cast if serialization fails
                                    if (entry.Value is T directValue)
                                    {
                                        return directValue;
                                    }
                                    return default;
                                }
                            }
                            
                            // Decompress the data
                            decompressStopwatch.Start();
                            var decompressedBytes = DecompressData(compressedBytes, entry.CompressionAlgorithm);
                            decompressStopwatch.Stop();
                            
                            // Convert bytes to the requested type
                            if (typeof(T) == typeof(byte[]))
                            {
                                return (T)(object)decompressedBytes;
                            }
                            else
                            {
                                try
                                {
                                    var decompressedValue = System.Text.Json.JsonSerializer.Deserialize<T>(decompressedBytes);
                                    return decompressedValue;
                                }
                                catch (Exception ex)
                                {
                                    MaintenanceLogs.Add(new MaintenanceLogEntry
                                    {
                                        Operation = "GetAsync_DeserializationAfterDecompression",
                                        Status = "Error",
                                        Metadata = new Dictionary<string, object>
                                        {
                                            ["error"] = ex.Message,
                                            ["key"] = key,
                                            ["algorithm"] = entry.CompressionAlgorithm.ToString(),
                                            ["decompressedSize"] = decompressedBytes?.Length ?? 0
                                        }
                                    });
                                    
                                    // Try one more approach - direct string conversion if appropriate
                                    if (typeof(T) == typeof(string) && decompressedBytes != null)
                                    {
                                        try
                                        {
                                            return (T)(object)System.Text.Encoding.UTF8.GetString(decompressedBytes);
                                        }
                                        catch
                                        {
                                            // Ignore and fall through to default
                                        }
                                    }
                                    
                                    return default;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            decompressStopwatch.Stop();
                            MaintenanceLogs.Add(new MaintenanceLogEntry
                            {
                                Operation = "GetAsync_Decompression",
                                Status = "Error",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["error"] = ex.Message,
                                    ["key"] = key,
                                    ["algorithm"] = entry.CompressionAlgorithm.ToString(),
                                    ["original_size"] = entry.Size,
                                    ["compressed_size"] = entry.CompressedSize
                                }
                            });
                            
                            // If decompression fails, see if we can return the value directly
                            if (entry.Value is T typedValue)
                            {
                                return typedValue;
                            }
                            
                            // We couldn't recover the value
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
                        var deserialized = System.Text.Json.JsonSerializer.Deserialize<T>(bytes);
                        return deserialized;
                    }
                    catch (Exception ex)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "GetAsync_Deserialization",
                            Status = "Error",
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = ex.Message,
                                ["key"] = key,
                                ["value_type"] = entry.Value?.GetType().Name ?? "null",
                                ["target_type"] = typeof(T).Name
                            }
                        });
                        
                        // Last resort: try direct cast
                        try
                        {
                            return (T)entry.Value;
                        }
                        catch
                        {
                            return default;
                        }
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
                
                // Record detailed metrics if hit was for a compressed entry
                if (isHit && wasCompressed && _metricsService != null)
                {
                    _ = _metricsService.RecordCustomMetric($"decompression_time_{algorithm}", decompressStopwatch.ElapsedMilliseconds);
                    _ = _metricsService.RecordCustomMetric("compression_ratio", (double)compressedSize / originalSize);
                    _ = _metricsService.RecordCustomMetric("bytes_saved_per_access", originalSize - compressedSize);
                    
                    // Performance impact tracking
                    var decompressionOverhead = decompressStopwatch.ElapsedMilliseconds;
                    var totalTime = stopwatch.ElapsedMilliseconds;
                    var overheadPercent = decompressionOverhead * 100.0 / Math.Max(1, totalTime);
                    
                    _ = _metricsService.RecordCustomMetric("decompression_overhead_percent", overheadPercent);
                    
                    // Alert if decompression is taking excessive time
                    if (decompressionOverhead > 50) // More than 50ms is concerning
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "DecompressionPerformance",
                            Status = "Warning",
                            Metadata = new Dictionary<string, object>
                            {
                                ["key"] = key,
                                ["algorithm"] = algorithm.ToString(),
                                ["decompression_time_ms"] = decompressionOverhead,
                                ["total_access_time_ms"] = totalTime,
                                ["overhead_percent"] = Math.Round(overheadPercent, 2),
                                ["original_size"] = originalSize,
                                ["compressed_size"] = compressedSize
                            }
                        });
                    }
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
                    try
                    {
                        serializedValue = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
                    }
                    catch (Exception ex)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "Serialization",
                            Status = "Error",
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = ex.Message,
                                ["key"] = key,
                                ["type"] = typeof(T).Name
                            }
                        });
                        
                        // Fallback: store as-is without compression if serialization fails
                        _cache[key] = new CacheEntry
                        {
                            Value = value,
                            ExpirationTime = expiryTime,
                            Size = 0, // Size unknown
                            CompressedSize = 0,
                            IsCompressed = false,
                            CompressionAlgorithm = CompressionAlgorithm.None,
                            CacheFormatVersion = 2
                        };
                        return;
                    }
                }
                
                var originalSize = serializedValue?.Length ?? 0;
                
                // Determine if this entry should be compressed
                var shouldCompress = _compressionEnabled && originalSize >= _minSizeForCompression;
                
                // Detect content type first for better algorithm selection
                var contentType = DetectContentType(serializedValue, typeof(T).Name);
                
                // Determine optimal compression algorithm and level for this data
                var compressionAlgorithm = shouldCompress 
                    ? DetermineOptimalCompressionAlgorithm(originalSize, typeof(T).Name) 
                    : CompressionAlgorithm.None;
                
                var compressionLevel = shouldCompress
                    ? DetermineOptimalCompressionLevel(originalSize, typeof(T).Name)
                    : CompressionLevel.Fastest; // Default, though not used if not compressing
                    
                // If adaptive compression is enabled, check for content-type specific compression level
                if (shouldCompress && _adaptiveCompression && 
                    _cacheSettings.ContentTypeCompressionLevelMap.TryGetValue(contentType, out var contentTypeLevel))
                {
                    compressionLevel = contentTypeLevel;
                }
                
                // Apply compression if needed
                byte[] compressedValue = null;
                long compressedSize = 0;
                bool compressionSucceeded = false;
                var compressionStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                if (shouldCompress && compressionAlgorithm != CompressionAlgorithm.None)
                {
                    try
                    {
                        compressedValue = CompressData(serializedValue, compressionAlgorithm, compressionLevel);
                        compressedSize = compressedValue?.Length ?? 0;
                        
                        // Only use compression if it actually reduces size by at least 10%
                        // This ensures we don't waste storage with minimal compression gains
                        compressionSucceeded = compressedSize > 0 && compressedSize < originalSize * 0.9;
                    }
                    catch (Exception ex)
                    {
                        // Log compression error but continue without compression
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "Compression",
                            Status = "Error",
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = ex.Message,
                                ["key"] = key,
                                ["algorithm"] = compressionAlgorithm.ToString(),
                                ["data_size"] = originalSize
                            }
                        });
                        
                        // Reset compression flags
                        compressionSucceeded = false;
                        compressionAlgorithm = CompressionAlgorithm.None;
                    }
                }
                compressionStopwatch.Stop();
                
                // If compression was attempted but didn't reduce size enough, use original value
                if (shouldCompress && !compressionSucceeded)
                {
                    compressionAlgorithm = CompressionAlgorithm.None;
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
                
                // Log detailed compression metrics
                if (shouldCompress)
                {
                    var byteSaved = compressionSucceeded ? originalSize - compressedSize : 0;
                    var compressionRatio = compressionSucceeded ? (double)compressedSize / originalSize : 1.0;
                    
                    if (_metricsService != null)
                    {
                        _ = _metricsService.RecordCustomMetric("compression_attempt", 1);
                        _ = _metricsService.RecordCustomMetric("compression_success", compressionSucceeded ? 1 : 0);
                        if (compressionSucceeded)
                        {
                            _ = _metricsService.RecordCustomMetric("compression_ratio", compressionRatio);
                            _ = _metricsService.RecordCustomMetric("compression_bytes_saved", byteSaved);
                            _ = _metricsService.RecordCustomMetric("compression_time_ms", compressionStopwatch.ElapsedMilliseconds);
                        }
                    }
                    
                    // Only log successful compressions with significant savings, or update to every 100th entry to avoid log spam
                    bool shouldLog = (compressionSucceeded && byteSaved > 10 * 1024) || // > 10KB saved
                                   (_compressionStats[compressionAlgorithm.ToString()].TotalItems % 100 == 0);
                    
                    if (shouldLog)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "Compression",
                            Status = compressionSucceeded ? "Success" : "Skipped",
                            Metadata = new Dictionary<string, object>
                            {
                                ["key"] = key,
                                ["original_size"] = originalSize,
                                ["compressed_size"] = compressionSucceeded ? compressedSize : originalSize,
                                ["compression_ratio"] = Math.Round(compressionRatio, 4),
                                ["bytes_saved"] = byteSaved,
                                ["algorithm"] = compressionAlgorithm.ToString(),
                                ["level"] = compressionLevel.ToString(),
                                ["time_ms"] = compressionStopwatch.ElapsedMilliseconds,
                                ["reason_skipped"] = !compressionSucceeded ? "Insufficient size reduction" : null
                            }
                        });
                    }
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
                            ["compression_applied"] = compressionSucceeded,
                            ["original_size"] = originalSize,
                            ["compressed_size"] = compressionSucceeded ? compressedSize : originalSize
                        }
                    });
                }
                
                // Notify metrics service about this write operation
                if (_metricsService != null)
                {
                    _ = _metricsService.RecordCacheWriteMetric(key, stopwatch.ElapsedMilliseconds);
                    
                    // Record size metrics
                    _ = _metricsService.RecordCustomMetric("cache_entry_size", originalSize);
                    if (compressionSucceeded)
                    {
                        _ = _metricsService.RecordCustomMetric("cache_compressed_entry_size", compressedSize);
                    }
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
                        ["stack_trace"] = ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0)),
                        ["key"] = key,
                        ["type"] = typeof(T).Name
                    }
                });
                throw;
            }
            finally
            {
                stopwatch.Stop();
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
        
        /// <summary>
        /// Migrates legacy cache entries to compressed format
        /// </summary>
        private async Task MigrateLegacyCacheEntries()
        {
            // Skip if compression is disabled
            if (!_compressionEnabled || !_cacheSettings.UpgradeUncompressedEntries)
            {
                return;
            }
            
            MaintenanceLogs.Add(new MaintenanceLogEntry
            {
                Operation = "LegacyCacheMigration",
                Status = "Started",
                Metadata = new Dictionary<string, object>
                {
                    ["cache_entry_count"] = _cache.Count,
                    ["compression_algorithm"] = _compressionAlgorithm.ToString(),
                    ["compression_level"] = _compressionLevel.ToString(),
                    ["adaptive_compression"] = _adaptiveCompression
                }
            });
            
            int migratedCount = 0;
            long originalSize = 0;
            long compressedSize = 0;
            var processedKeys = new List<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Process in batches to avoid locking the cache for too long
                foreach (var keyBatch in _cache.Keys.ToList().Chunk(100))
                {
                    foreach (var key in keyBatch)
                    {
                        if (!_cache.TryGetValue(key, out var entry) || !IsLegacyCacheEntry(entry))
                        {
                            continue;
                        }
                        
                        try
                        {
                            // Skip entries that are already expired
                            if (entry.IsExpired)
                            {
                                continue;
                            }
                            
                            // Track original size
                            originalSize += entry.Size;
                            
                            // Skip small entries if below minimum size threshold
                            if (entry.Size < _minSizeForCompression)
                            {
                                continue;
                            }
                            
                            // Re-serialize the entry to apply compression
                            var timeToExpiry = entry.ExpirationTime - DateTime.UtcNow;
                            if (timeToExpiry <= TimeSpan.Zero)
                            {
                                continue; // Skip if already expired
                            }
                            
                            // Get current value from cache
                            var currentValue = entry.Value;
                            
                            // Rewrite the cache entry with compression
                            await SetAsync(key, currentValue, timeToExpiry);
                            
                            // Get the updated entry to measure size reduction
                            if (_cache.TryGetValue(key, out var updatedEntry) && updatedEntry.IsCompressed)
                            {
                                compressedSize += updatedEntry.CompressedSize;
                                migratedCount++;
                                processedKeys.Add(key);
                            }
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
                    
                    // Add a small delay between batches to avoid excessive CPU usage
                    await Task.Delay(100);
                }
            }
            finally
            {
                stopwatch.Stop();
                
                // Log migration results
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "LegacyCacheMigration",
                    Status = migratedCount > 0 ? "Success" : "NothingToMigrate",
                    Metadata = new Dictionary<string, object>
                    {
                        ["migrated_entries"] = migratedCount,
                        ["processed_keys"] = processedKeys,
                        ["time_taken_ms"] = stopwatch.ElapsedMilliseconds,
                        ["original_size_bytes"] = originalSize,
                        ["compressed_size_bytes"] = compressedSize,
                        ["bytes_saved"] = originalSize - compressedSize,
                        ["percent_saved"] = originalSize > 0 
                            ? Math.Round(100.0 * (originalSize - compressedSize) / originalSize, 2) 
                            : 0
                    }
                });
                
                // If compression provided good results, log a recommendation
                if (migratedCount > 0 && originalSize > 0)
                {
                    var compressionRatio = (double)compressedSize / originalSize;
                    if (compressionRatio < 0.5) // More than 50% reduction
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "CompressionRecommendation",
                            Status = "HighSavings",
                            Metadata = new Dictionary<string, object>
                            {
                                ["message"] = "Compression is highly effective for your cache data. Consider increasing the cache size limit to store more entries.",
                                ["compression_ratio"] = Math.Round(compressionRatio, 4),
                                ["recommended_setting"] = $"CompressionEnabled = true, CompressionAlgorithm = {_compressionAlgorithm}, CompressionLevel = {_compressionLevel}"
                            }
                        });
                    }
                }
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
        /// <summary>
        /// Compresses data using the specified algorithm and compression level
        /// </summary>
        private byte[] CompressData(byte[] data, CompressionAlgorithm algorithm, CompressionLevel level)
        {
            if (data == null || data.Length == 0)
                return data;
                
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Apply CPU and memory resource constraints to auto-adjust compression level if enabled
                if (_cacheSettings.AutoAdjustCompressionLevel)
                {
                    // Try to get CPU metric if metrics service is available
                    double cpuUsage = 0;
                    if (_metricsService != null)
                    {
                        var cpuMetric = _metricsService.GetCustomMetric("cpu_usage_percent");
                        if (cpuMetric != null)
                        {
                            cpuUsage = cpuMetric.LastValue;
                        }
                    }
                    
                    // If CPU usage is high (> 80%), downgrade to faster compression
                    if (cpuUsage > 80) 
                    {
                        // Downgrade to faster compression
                        level = CompressionLevel.Fastest;
                        
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "CompressionAutoTune",
                            Status = "LevelDowngraded",
                            Metadata = new Dictionary<string, object>
                            {
                                ["reason"] = "High CPU usage",
                                ["cpu_usage"] = cpuUsage,
                                ["original_level"] = level.ToString(),
                                ["adjusted_level"] = CompressionLevel.Fastest.ToString()
                            }
                        });
                    }
                    
                    // For very large data, always use faster compression to avoid memory pressure
                    if (data.Length > _cacheSettings.MaxSizeForHighCompressionBytes && level == CompressionLevel.SmallestSize)
                    {
                        level = CompressionLevel.Optimal;
                        
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "CompressionAutoTune",
                            Status = "LevelAdjusted",
                            Metadata = new Dictionary<string, object>
                            {
                                ["reason"] = "Large data size",
                                ["data_size"] = data.Length,
                                ["threshold"] = _cacheSettings.MaxSizeForHighCompressionBytes,
                                ["adjusted_level"] = CompressionLevel.Optimal.ToString()
                            }
                        });
                    }
                }
                
                using var outputStream = new MemoryStream();
                bool useMemoryEfficient = data.Length > 10 * 1024 * 1024; // For very large data (>10MB)
                
                switch (algorithm)
                {
                    case CompressionAlgorithm.GZip:
                        using (var gzipStream = new GZipStream(outputStream, level))
                        {
                            if (useMemoryEfficient)
                            {
                                // Use smaller chunks for very large data to reduce memory pressure
                                const int chunkSize = 81920; // 80KB chunks
                                for (int offset = 0; offset < data.Length; offset += chunkSize)
                                {
                                    int bytesToWrite = Math.Min(chunkSize, data.Length - offset);
                                    gzipStream.Write(data, offset, bytesToWrite);
                                }
                            }
                            else
                            {
                                gzipStream.Write(data, 0, data.Length);
                            }
                            gzipStream.Flush();
                        }
                        break;
                        
                    case CompressionAlgorithm.Brotli:
                        using (var brotliStream = new BrotliStream(outputStream, level))
                        {
                            if (useMemoryEfficient)
                            {
                                // Use smaller chunks for very large data to reduce memory pressure
                                const int chunkSize = 81920; // 80KB chunks
                                for (int offset = 0; offset < data.Length; offset += chunkSize)
                                {
                                    int bytesToWrite = Math.Min(chunkSize, data.Length - offset);
                                    brotliStream.Write(data, offset, bytesToWrite);
                                }
                            }
                            else
                            {
                                brotliStream.Write(data, 0, data.Length);
                            }
                            brotliStream.Flush();
                        }
                        break;
                        
                    case CompressionAlgorithm.None:
                    default:
                        return data;
                }
                
                var result = outputStream.ToArray();
                
                // Only return compressed data if it's actually smaller by the configured threshold
                double compressionRatio = (double)result.Length / data.Length;
                
                // Update compression stats regardless of whether we use the compressed result or not
                stopwatch.Stop();
                UpdateCompressionStats(algorithm.ToString(), data.Length, result.Length, stopwatch.ElapsedMilliseconds, 0, compressionRatio < _cacheSettings.MinCompressionRatio);
                
                if (compressionRatio < _cacheSettings.MinCompressionRatio)
                {
                    // Log successful compression
                    if (_cacheSettings.TrackCompressionMetrics)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "Compression",
                            Status = "Success",
                            Metadata = new Dictionary<string, object>
                            {
                                ["algorithm"] = algorithm.ToString(),
                                ["level"] = level.ToString(),
                                ["original_size"] = data.Length,
                                ["compressed_size"] = result.Length,
                                ["ratio"] = compressionRatio,
                                ["time_ms"] = stopwatch.ElapsedMilliseconds,
                                ["bytes_saved"] = data.Length - result.Length
                            }
                        });
                    }
                    
                    // Record our success for adaptive algorithm selection
                    if (_metricsService != null)
                    {
                        _ = _metricsService.RecordCustomMetric($"compression_success_{algorithm}", 1);
                        _ = _metricsService.RecordCustomMetric($"compression_ratio_{algorithm}", compressionRatio);
                        _ = _metricsService.RecordCustomMetric($"compression_time_{algorithm}", stopwatch.ElapsedMilliseconds);
                        _ = _metricsService.RecordCustomMetric($"bytes_saved_{algorithm}", data.Length - result.Length);
                    }
                    
                    return result;
                }
                
                // If compression didn't reduce size enough, return original data
                if (_metricsService != null)
                {
                    _ = _metricsService.RecordCustomMetric($"compression_skipped_{algorithm}", 1);
                }
                
                return data;
            }
            catch (Exception ex)
            {
                // Log error and return original data if compression fails
                stopwatch.Stop();
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "Compression",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["algorithm"] = algorithm.ToString(),
                        ["level"] = level.ToString(),
                        ["data_size"] = data.Length,
                        ["stack_trace"] = ex.StackTrace?.Substring(0, Math.Min(ex.StackTrace?.Length ?? 0, 500))
                    }
                });
                
                if (_metricsService != null)
                {
                    _ = _metricsService.RecordCustomMetric($"compression_error_{algorithm}", 1);
                }
                
                return data;
            }
        }
        
        /// <summary>
        /// Decompresses data that was compressed with the specified algorithm
        /// </summary>
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
                
                // Use buffer for more efficient memory usage during decompression
                byte[] buffer = null;
                
                // Optimize buffer size based on compressed data size
                if (data.Length > 1024 * 1024) // > 1MB
                {
                    buffer = new byte[81920]; // 80KB buffer for large data
                }
                else if (data.Length > 100 * 1024) // > 100KB
                {
                    buffer = new byte[32768]; // 32KB buffer for medium data
                }
                else if (data.Length > 10 * 1024) // > 10KB
                {
                    buffer = new byte[8192]; // 8KB buffer for small data
                }
                // For very small data, let the CopyTo method handle buffer allocation
                
                switch (algorithm)
                {
                    case CompressionAlgorithm.GZip:
                        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                        {
                            if (buffer != null)
                            {
                                int read;
                                while ((read = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outputStream.Write(buffer, 0, read);
                                }
                            }
                            else
                            {
                                gzipStream.CopyTo(outputStream);
                            }
                        }
                        break;
                        
                    case CompressionAlgorithm.Brotli:
                        using (var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress))
                        {
                            if (buffer != null)
                            {
                                int read;
                                while ((read = brotliStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outputStream.Write(buffer, 0, read);
                                }
                            }
                            else
                            {
                                brotliStream.CopyTo(outputStream);
                            }
                        }
                        break;
                        
                    default:
                        return data;
                }
                
                var result = outputStream.ToArray();
                
                // Update decompression stats
                stopwatch.Stop();
                var decompressionTime = stopwatch.ElapsedMilliseconds;
                UpdateCompressionStats(algorithm.ToString(), result.Length, data.Length, 0, decompressionTime);
                
                // Track memory usage and performance for large decompression operations
                if (result.Length > 5 * 1024 * 1024) // > 5MB uncompressed
                {
                    var memoryAfter = GC.GetTotalMemory(false);
                    
                    // Log memory usage for large decompression operations
                    if (_cacheSettings.TrackCompressionMetrics)
                    {
                        MaintenanceLogs.Add(new MaintenanceLogEntry
                        {
                            Operation = "LargeDecompression",
                            Status = "Success",
                            Metadata = new Dictionary<string, object>
                            {
                                ["algorithm"] = algorithm.ToString(),
                                ["compressed_size"] = data.Length,
                                ["uncompressed_size"] = result.Length,
                                ["memory_used"] = memoryAfter,
                                ["decompression_time_ms"] = decompressionTime,
                                ["compression_ratio"] = (double)data.Length / result.Length
                            }
                        });
                    }
                    
                    // Record metrics for large decompressions
                    if (_metricsService != null)
                    {
                        _ = _metricsService.RecordCustomMetric("large_decompression_count", 1);
                        _ = _metricsService.RecordCustomMetric("large_decompression_time_ms", decompressionTime);
                        _ = _metricsService.RecordCustomMetric("large_decompression_size_mb", result.Length / (1024.0 * 1024.0));
                    }
                }
                
                // Log successful decompression for non-trivial operations
                if (_cacheSettings.TrackCompressionMetrics && decompressionTime > 5) 
                {
                    MaintenanceLogs.Add(new MaintenanceLogEntry
                    {
                        Operation = "Decompression",
                        Status = "Success",
                        Metadata = new Dictionary<string, object>
                        {
                            ["algorithm"] = algorithm.ToString(),
                            ["compressed_size"] = data.Length,
                            ["original_size"] = result.Length,
                            ["ratio"] = result.Length > 0 ? (double)data.Length / result.Length : 1.0,
                            ["time_ms"] = decompressionTime
                        }
                    });
                    
                    // Record metrics for decompression performance
                    if (_metricsService != null)
                    {
                        _ = _metricsService.RecordCustomMetric($"decompression_time_{algorithm}", decompressionTime);
                        
                        // Track performance ratio (bytes decompressed per ms)
                        if (decompressionTime > 0)
                        {
                            var bytesPerMs = result.Length / decompressionTime;
                            _ = _metricsService.RecordCustomMetric($"decompression_throughput_{algorithm}", bytesPerMs);
                        }
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                // Log error and return original data if decompression fails
                stopwatch.Stop();
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "Decompression",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["algorithm"] = algorithm.ToString(),
                        ["data_size"] = data.Length,
                        ["time_spent_ms"] = stopwatch.ElapsedMilliseconds,
                        ["stack_trace"] = ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0))
                    }
                });
                
                if (_metricsService != null)
                {
                    _ = _metricsService.RecordCustomMetric($"decompression_error_{algorithm}", 1);
                    _ = _metricsService.RecordCustomMetric("decompression_errors_total", 1);
                }
                
                return data;
            }
        }
        
        /// <summary>
        /// Updates compression statistics with the results of a compression or decompression operation
        /// </summary>
        private void UpdateCompressionStats(string algorithm, long originalSize, long compressedSize, 
                                           double compressionTimeMs, double decompressionTimeMs, 
                                           bool wasCompressed = true)
        {
            if (!_cacheSettings.TrackCompressionMetrics)
                return;
                
            lock (_compressionStats) // Thread safety for stats update
            {
                if (!_compressionStats.ContainsKey(algorithm))
                {
                    _compressionStats[algorithm] = new CompressionMetrics();
                }
                
                var stats = _compressionStats[algorithm];
                ContentType contentType = _lastContentType;
                
                if (compressionTimeMs > 0)
                {
                    // Compression stats update
                    stats.TotalItems++;
                    if (wasCompressed && compressedSize < originalSize)
                    {
                        stats.CompressedItems++;
                        stats.OriginalSizeBytes += originalSize;
                        stats.CompressedSizeBytes += compressedSize;
                        
                        // Record metrics for this particular compression operation
                        if (_metricsService != null)
                        {
                            _ = _metricsService.RecordCustomMetric(
                                $"compression_ratio_{algorithm}", 
                                (double)compressedSize / originalSize
                            );
                            
                            _ = _metricsService.RecordCustomMetric(
                                $"compression_savings_{algorithm}", 
                                originalSize - compressedSize
                            );
                            
                            _ = _metricsService.RecordCustomMetric(
                                $"compression_time_{algorithm}", 
                                compressionTimeMs
                            );
                            
                            // Record efficiency score
                            double efficiencyScore = (1.0 - ((double)compressedSize / originalSize)) * 
                                                    (100.0 / Math.Max(1.0, compressionTimeMs));
                            _ = _metricsService.RecordCustomMetric(
                                $"compression_efficiency_{algorithm}",
                                efficiencyScore
                            );
                        }
                        
                        // Update content type stats if we have information about the content type
                        if (_lastContentType != ContentType.Unknown)
                        {
                            contentType = _lastContentType;
                            if (!_compressionByContentType.ContainsKey(contentType))
                            {
                                _compressionByContentType[contentType] = new CompressionMetrics();
                            }
                            
                            var contentTypeStats = _compressionByContentType[contentType];
                            contentTypeStats.TotalItems++;
                            contentTypeStats.CompressedItems++;
                            contentTypeStats.OriginalSizeBytes += originalSize;
                            contentTypeStats.CompressedSizeBytes += compressedSize;
                            
                            // Update average compression time
                            if (contentTypeStats.TotalItems > 1)
                            {
                                contentTypeStats.AverageCompressionTimeMs = 
                                    (contentTypeStats.AverageCompressionTimeMs * 0.9) + (compressionTimeMs * 0.1);
                            }
                            else
                            {
                                contentTypeStats.AverageCompressionTimeMs = compressionTimeMs;
                            }
                        }
                    }
                    else
                    {
                        // Track original size for both compressed and uncompressed items
                        stats.OriginalSizeBytes += originalSize;
                        stats.CompressedSizeBytes += originalSize; // Use original size since compression was skipped or ineffective
                        
                        // Update content type stats even for uncompressed items
                        if (_lastContentType != ContentType.Unknown)
                        {
                            contentType = _lastContentType;
                            if (!_compressionByContentType.ContainsKey(contentType))
                            {
                                _compressionByContentType[contentType] = new CompressionMetrics();
                            }
                            
                            var contentTypeStats = _compressionByContentType[contentType];
                            contentTypeStats.TotalItems++;
                            contentTypeStats.OriginalSizeBytes += originalSize;
                            contentTypeStats.CompressedSizeBytes += originalSize;
                        }
                    }
                    
                    // Update average compression time (weighted moving average with more weight to recent values)
                    if (stats.TotalItems > 1)
                    {
                        // Use 0.9 as weight for old average, 0.1 for new value for smoother transition
                        stats.AverageCompressionTimeMs = (stats.AverageCompressionTimeMs * 0.9) + (compressionTimeMs * 0.1);
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
                        // Use 0.9 as weight for old average, 0.1 for new value for smoother transition
                        stats.AverageDecompressionTimeMs = (stats.AverageDecompressionTimeMs * 0.9) + (decompressionTimeMs * 0.1);
                        
                        // Record metrics for this particular decompression operation
                        if (_metricsService != null)
                        {
                            _ = _metricsService.RecordCustomMetric(
                                $"decompression_time_{algorithm}", 
                                decompressionTimeMs
                            );
                        }
                        
                        // Update content type decompression stats
                        if (contentType != ContentType.Unknown && _compressionByContentType.ContainsKey(contentType))
                        {
                            var contentTypeStats = _compressionByContentType[contentType];
                            if (contentTypeStats.TotalItems > 0)
                            {
                                contentTypeStats.AverageDecompressionTimeMs = 
                                    (contentTypeStats.AverageDecompressionTimeMs * 0.9) + (decompressionTimeMs * 0.1);
                            }
                            else
                            {
                                contentTypeStats.AverageDecompressionTimeMs = decompressionTimeMs;
                            }
                        }
                    }
                    else
                    {
                        stats.AverageDecompressionTimeMs = decompressionTimeMs;
                    }
                }
                
                // Update compression history periodically
                if (ShouldAddCompressionHistoryEntry())
                {
                    var entry = new CompressionHistoryEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        CompressionRatio = stats.CompressionRatio,
                        BytesSaved = stats.BytesSaved,
                        CompressedItemPercentage = stats.TotalItems > 0 
                            ? 100.0 * stats.CompressedItems / stats.TotalItems 
                            : 0,
                        AverageCompressionTimeMs = stats.AverageCompressionTimeMs,
                        PrimaryAlgorithm = algorithm,
                        EfficiencyScore = stats.EfficiencyScore
                    };
                    
                    lock (_compressionHistory)
                    {
                        _compressionHistory.Add(entry);
                        
                        // Cleanup old history entries
                        var cutoffTime = DateTime.UtcNow.AddHours(-_cacheSettings.CompressionMetricsRetentionHours);
                        _compressionHistory.RemoveAll(h => h.Timestamp < cutoffTime);
                    }
                }
                
                // Periodically log summary statistics (every 100 operations)
                if (stats.TotalItems % 100 == 0)
                {
                    MaintenanceLogs.Add(new MaintenanceLogEntry
                    {
                        Operation = "CompressionStats",
                        Status = "Summary",
                        Metadata = new Dictionary<string, object>
                        {
                            ["algorithm"] = algorithm,
                            ["total_items"] = stats.TotalItems,
                            ["compressed_items"] = stats.CompressedItems,
                            ["original_size_mb"] = Math.Round(stats.OriginalSizeBytes / (1024.0 * 1024.0), 2),
                            ["compressed_size_mb"] = Math.Round(stats.CompressedSizeBytes / (1024.0 * 1024.0), 2),
                            ["avg_compression_time_ms"] = Math.Round(stats.AverageCompressionTimeMs, 2),
                            ["avg_decompression_time_ms"] = Math.Round(stats.AverageDecompressionTimeMs, 2),
                            ["compression_ratio"] = Math.Round(stats.CompressionRatio, 4),
                            ["savings_percent"] = Math.Round(stats.CompressionSavingPercent, 2),
                            ["efficiency_score"] = Math.Round(stats.EfficiencyScore, 4),
                            ["content_type"] = contentType.ToString()
                        }
                    });
                }
            }
        }
        
        // Helper method to determine if we should add a new history entry
        private bool ShouldAddCompressionHistoryEntry()
        {
            lock (_compressionHistory)
            {
                // Add an entry if this is the first one or if enough time has passed since the last one
                if (_compressionHistory.Count == 0)
                    return true;
                    
                var lastEntry = _compressionHistory.Last();
                return (DateTime.UtcNow - lastEntry.Timestamp).TotalMinutes >= 15; // Every 15 minutes
            }
        }
        
        /// <summary>
        /// Detects content type from data sample and type name
        /// </summary>
        private ContentType DetectContentType(byte[] data, string typeName)
        {
            if (data == null || data.Length < 16)
                return ContentType.Unknown;
                
            try {
                // Check for common JSON pattern at the beginning of data
                if (data.Length >= 2 && (data[0] == '{' && data[1] == '"' || data[0] == '[' && data[1] == '{'))
                {
                    _lastContentType = ContentType.TextJson;
                    return ContentType.TextJson;
                }
                
                // Check for XML declaration or root element
                if (data.Length >= 5 && 
                    (data[0] == '<' && data[1] == '?' && data[2] == 'x' && data[3] == 'm' && data[4] == 'l') ||
                    (data[0] == '<' && data[1] != '/' && data[1] != '!' && data[1] != '?'))
                {
                    _lastContentType = ContentType.TextXml;
                    return ContentType.TextXml;
                }
                
                // Check for HTML tags
                if (data.Length >= 14 && 
                    (string.Equals(System.Text.Encoding.ASCII.GetString(data, 0, 14), "<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                     data.Length >= 6 && string.Equals(System.Text.Encoding.ASCII.GetString(data, 0, 6), "<html>", StringComparison.OrdinalIgnoreCase)))
                {
                    _lastContentType = ContentType.TextHtml;
                    return ContentType.TextHtml;
                }
                
                // Check content using type name
                if (!string.IsNullOrEmpty(typeName))
                {
                    // Check for text-based content
                    if (typeName.Contains("String", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Text", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastContentType = ContentType.TextPlain;
                        return ContentType.TextPlain;
                    }
                    
                    // Check for image data
                    if (typeName.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Bitmap", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Jpeg", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Png", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastContentType = ContentType.Image;
                        return ContentType.Image;
                    }
                    
                    // Check for binary data
                    if (typeName.Contains("Byte[]", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Binary", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastContentType = ContentType.BinaryData;
                        return ContentType.BinaryData;
                    }
                    
                    // Check for common compressed formats
                    if (typeName.Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Compressed", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Gzip", StringComparison.OrdinalIgnoreCase) ||
                        typeName.Contains("Brotli", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastContentType = ContentType.CompressedData;
                        return ContentType.CompressedData;
                    }
                }
                
                // Check for probable text content by looking at the first 100 bytes
                // Text content should mostly contain printable ASCII characters
                if (data.Length >= 30)
                {
                    int textCharCount = 0;
                    int binaryCharCount = 0;
                    int sampleSize = Math.Min(100, data.Length);
                    
                    for (int i = 0; i < sampleSize; i++)
                    {
                        // ASCII printable chars (32-126) plus common whitespace (9, 10, 13)
                        if ((data[i] >= 32 && data[i] <= 126) || data[i] == 9 || data[i] == 10 || data[i] == 13)
                        {
                            textCharCount++;
                        }
                        else
                        {
                            binaryCharCount++;
                        }
                    }
                    
                    // If 80% or more are text characters, it's probably a text format
                    if (textCharCount >= sampleSize * 0.8)
                    {
                        _lastContentType = ContentType.TextPlain;
                        return ContentType.TextPlain;
                    }
                    
                    // If 60% or more are binary, it's probably binary data
                    if (binaryCharCount >= sampleSize * 0.6)
                    {
                        _lastContentType = ContentType.BinaryData;
                        return ContentType.BinaryData;
                    }
                }
                
                // Check for compressed data signatures
                if (data.Length >= 4 && data[0] == 0x1F && data[1] == 0x8B) // GZip signature
                {
                    _lastContentType = ContentType.CompressedData;
                    return ContentType.CompressedData;
                }
                
                if (data.Length >= 2 && data[0] == 0x50 && data[1] == 0x4B) // ZIP signature
                {
                    _lastContentType = ContentType.CompressedData;
                    return ContentType.CompressedData;
                }
                
                // Default to binary data for unknown formats
                _lastContentType = ContentType.BinaryData;
                return ContentType.BinaryData;
            }
            catch (Exception ex)
            {
                // Log error but don't fail content detection
                MaintenanceLogs.Add(new MaintenanceLogEntry
                {
                    Operation = "ContentTypeDetection",
                    Status = "Error",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["data_length"] = data?.Length ?? 0,
                        ["type_name"] = typeName ?? "null"
                    }
                });
                
                // Fall back to safer assumption
                _lastContentType = ContentType.BinaryData;
                return ContentType.BinaryData;
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
            
            // If we have a content type mapping available, use it
            if (_lastContentType != ContentType.Unknown && 
                _cacheSettings.ContentTypeAlgorithmMap.TryGetValue(_lastContentType, out var mappedAlgorithm))
            {
                return mappedAlgorithm;
            }
            
            // Use content-based heuristics for optimal algorithm selection
            if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true || 
                dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Text-based data compresses well with either algorithm
                
                // For very large text data, Brotli usually provides better compression ratio
                if (dataSize > 500 * 1024) // > 500KB
                {
                    return CompressionAlgorithm.Brotli;
                }
                
                // For medium size text data, use GZip (better performance)
                return CompressionAlgorithm.GZip;
            }
            
            // Data types that are likely already compressed
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
                var gzipTime = _compressionStats["GZip"].AverageCompressionTimeMs;
                var brotliTime = _compressionStats["Brotli"].AverageCompressionTimeMs;
                
                // If Brotli is significantly better in compression ratio (>15% better)
                if (brotliEfficiency < gzipEfficiency * 0.85)
                {
                    return CompressionAlgorithm.Brotli;
                }
                
                // If both are similar but GZip is much faster
                if (brotliEfficiency >= gzipEfficiency * 0.85 && gzipTime < brotliTime * 0.7)
                {
                    return CompressionAlgorithm.GZip;
                }
                
                // If GZip is only slightly worse but much faster
                if (brotliEfficiency < gzipEfficiency && brotliEfficiency >= gzipEfficiency * 0.9 && 
                    gzipTime < brotliTime * 0.5)
                {
                    return CompressionAlgorithm.GZip;
                }
                
                // Default to the better ratio algorithm
                return brotliEfficiency < gzipEfficiency ? CompressionAlgorithm.Brotli : CompressionAlgorithm.GZip;
            }
            
            // Data size based decisions when we don't have enough stats
            
            // For very large data, favor compression ratio (Brotli)
            if (dataSize > 5 * 1024 * 1024) // > 5MB
            {
                return CompressionAlgorithm.Brotli;
            }
            
            // For large data, consider if it might be compressible
            if (dataSize > 1 * 1024 * 1024) // > 1MB
            {
                // Analyze a sample of the data to determine if it's likely compressible
                if (dataType?.Contains("object", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("dictionary", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("array", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // These are likely complex data structures serialized to JSON
                    return CompressionAlgorithm.Brotli;
                }
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
            
            // Check CPU load if we have resource monitoring
            bool highCpuLoad = false;
            if (_metricsService != null)
            {
                try
                {
                    var cpuLoad = _metricsService.GetCustomMetric("cpu_usage_percent")?.LastValue ?? 0;
                    highCpuLoad = cpuLoad > 80; // Consider CPU load high if > 80%
                    
                    // If system is under high CPU pressure, use faster compression regardless of other factors
                    if (highCpuLoad)
                    {
                        return CompressionLevel.Fastest;
                    }
                }
                catch
                {
                    // Ignore - metrics service might not be fully operational
                }
            }
            
            // Size-based adaptive compression
            
            // For extremely large data, always use fastest compression
            if (dataSize > 50 * 1024 * 1024) // 50MB
            {
                return CompressionLevel.Fastest;
            }
            
            // For very large data (10-50MB), balance between size and speed
            if (dataSize > 10 * 1024 * 1024) // 10MB
            {
                // For highly compressible data types, it might still be worth using optimal compression
                if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return CompressionLevel.Optimal;
                }
                
                return CompressionLevel.Fastest;
            }
            
            // For large data (1-10MB), use optimal approach for most cases
            if (dataSize > 1 * 1024 * 1024) // 1MB
            {
                // Highly compressible text-based formats benefit from better compression
                if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // For very large text files, consider CPU usage vs storage savings
                    if (dataSize > 5 * 1024 * 1024) // > 5MB
                    {
                        return CompressionLevel.Optimal;
                    }
                    
                    // Smaller text files can use higher compression
                    return CompressionLevel.SmallestSize;
                }
                
                // For binary data that might compress poorly, use fastest
                if (dataType?.Contains("binary", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("byte", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return CompressionLevel.Fastest;
                }
                
                // Default for large data
                return CompressionLevel.Optimal;
            }
            
            // For medium data (100KB-1MB), optimize for compression ratio
            if (dataSize > 100 * 1024) // 100KB
            {
                // Text-based formats compress very well, use maximum compression
                if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ||
                    dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return CompressionLevel.SmallestSize;
                }
                
                // For other types, use optimal balance
                return CompressionLevel.Optimal;
            }
            
            // For smaller data (< 100KB), use maximum compression if it's a format that compresses well
            if (dataType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ||
                dataType?.Contains("string", StringComparison.OrdinalIgnoreCase) == true)
            {
                return CompressionLevel.SmallestSize;
            }
            
            // Learn from past compression statistics if available
            if (_compressionStats.ContainsKey("GZip") && _compressionStats["GZip"].TotalItems > 20)
            {
                // Use stats to optimize for specific data characteristics
                var gzipStats = _compressionStats["GZip"];
                
                // If we consistently get excellent compression (ratio < 0.3), 
                // the data is highly compressible and worth more CPU time
                if (gzipStats.CompressionRatio < 0.3)
                {
                    return CompressionLevel.SmallestSize;
                }
                
                // If compression isn't very effective (ratio > 0.7), 
                // don't waste CPU time on maximum compression
                if (gzipStats.CompressionRatio > 0.7)
                {
                    return CompressionLevel.Fastest;
                }
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
            
            /// <summary>
            /// Bytes saved due to compression
            /// </summary>
            public long BytesSaved => IsCompressed && Size > CompressedSize ? Size - CompressedSize : 0;
            
            /// <summary>
            /// Percent of storage saved by compression
            /// </summary>
            public double CompressionSavingPercent => 
                IsCompressed && Size > 0 
                    ? Math.Round(100.0 * (Size - CompressedSize) / Size, 2) 
                    : 0;
        }
    }
}