using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class FileBasedCacheProvider : ICacheProvider
    {
        private ICacheEvictionPolicy _evictionPolicy;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly MLPredictionService _mlPredictionService;
        private readonly MetricsCollectionService _metricsService;
        private Dictionary<string, double> _resourceLimits;
        private readonly object _resourceLock = new object();
        private readonly Dictionary<string, CacheEntry> _cache;
        private readonly Dictionary<string, CacheAccessStats> _accessStats;

        // IEncryptionProvider is not necessary for basic functionality
        public List<MaintenanceLogEntry> MaintenanceLogs { get; }

        public FileBasedCacheProvider(
            IOptions<CacheSettings> cacheSettings = null,
            MetricsCollectionService metricsService = null,
            MLPredictionService mlPredictionService = null)
        {
            _evictionPolicy = new DummyEvictionPolicy();
            _mlPredictionService = mlPredictionService;
            _metricsService = metricsService;
            _circuitBreaker = new CircuitBreaker();
            MaintenanceLogs = new List<MaintenanceLogEntry>();
            _cache = new Dictionary<string, CacheEntry>();
            _accessStats = new Dictionary<string, CacheAccessStats>();
            _resourceLimits = new Dictionary<string, double>
            {
                ["memory"] = 100,  // Default 100% of base allocation
                ["cpu"] = 100,     // Default 100% of base allocation
                ["storage"] = 100  // Default 100% of base allocation
            };
            
            // Register this instance with the metrics service if provided
            if (_metricsService != null)
            {
                _ = AddEventListener(_metricsService);
            }
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

        // Small cache eviction event classes
        public class CacheEvictionEvent
        {
            public string Reason { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        }

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
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                return (T)entry.Value;
            }
            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            var expiryTime = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(24));
            _cache[key] = new CacheEntry
            {
                Value = value,
                ExpirationTime = expiryTime,
                Size = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value).Length
            };
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
            // Return basic cache stats
            return new CacheStats
            {
                Hits = 0,
                Misses = 0,
                CachedItemCount = _cache.Count,
                StorageUsageBytes = _cache.Sum(c => c.Value.Size),
                QueryTypeStats = new Dictionary<string, long>(),
                AverageResponseTimes = new Dictionary<string, double>(),
                PerformanceByQueryType = new Dictionary<string, CachePerformanceMetrics>(),
                InvalidationHistory = new List<CacheInvalidationEvent>(),
                InvalidationCount = 0,
                TopQueries = _accessStats.OrderByDescending(a => a.Value.AccessCount)
                    .Take(5)
                    .ToDictionary(a => a.Key, a => (int)a.Value.AccessCount),
                LastStatsUpdate = DateTime.UtcNow
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

        public async Task PreWarmKey(string key)
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
                EvictionStrategy.Adaptive => new AdaptiveEvictionPolicy(_accessStats),
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
            // Calculate total size of cache entries
            return _cache.Sum(entry => entry.Value.Size);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return _evictionPolicy.GetEvictionStats();
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _evictionPolicy.GetRecentEvictions(count);
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

        private class CacheEntry
        {
            public object Value { get; set; }
            public DateTime ExpirationTime { get; set; }
            public long Size { get; set; }
            public bool IsExpired => DateTime.UtcNow > ExpirationTime;
        }

        private class CacheAccessStats
        {
            public long AccessCount { get; set; }
            public long PreWarmCount { get; set; }
            public DateTime LastAccess { get; set; }
        }
    }
}