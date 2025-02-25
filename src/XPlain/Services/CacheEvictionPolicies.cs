using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class LRUEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9; // 90% capacity
        private double _pressureThreshold = 0.7; // 70% capacity triggers early eviction actions

        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public Task<bool> UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
            return Task.FromResult(true);
        }

        public Task<bool> UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
            return Task.FromResult(true);
        }

        public Task<bool> ForceEviction(long bytesToFree)
        {
            // This implementation is a placeholder
            // The actual eviction is handled by the cache provider
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["items_tracked"] = _lastAccessTimes.Count,
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }

        public void RecordAccess(string key)
        {
            _lastAccessTimes.AddOrUpdate(key, DateTime.UtcNow, (_, __) => DateTime.UtcNow);
        }
    }

    public class SizeWeightedEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, long> _itemSizes = new();
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.7;
        
        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public Task<bool> UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
            return Task.FromResult(true);
        }

        public Task<bool> UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
            return Task.FromResult(true);
        }

        public Task<bool> ForceEviction(long bytesToFree)
        {
            // Placeholder implementation
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["items_tracked"] = _itemSizes.Count,
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }
        
        public void UpdateSize(string key, long size)
        {
            _itemSizes[key] = size;
        }
    }

    public class HitRateWeightedEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, int> _accessCounts = new();
        private readonly Dictionary<string, CacheAccessStats> _accessStats;
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.7;
        
        public HitRateWeightedEvictionPolicy(Dictionary<string, CacheAccessStats> accessStats)
        {
            _accessStats = accessStats;
        }
        
        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public Task<bool> UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
            return Task.FromResult(true);
        }

        public Task<bool> UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
            return Task.FromResult(true);
        }

        public Task<bool> ForceEviction(long bytesToFree)
        {
            // Placeholder implementation
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["items_tracked"] = _accessCounts.Count,
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }
        
        public void RecordAccess(string key)
        {
            _accessCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
        }
    }
    
    public class AdaptiveEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, CacheAccessStats> _accessStats;
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.7;
        
        public AdaptiveEvictionPolicy(Dictionary<string, CacheAccessStats> accessStats)
        {
            _accessStats = accessStats;
        }
        
        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public Task<bool> UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
            return Task.FromResult(true);
        }

        public Task<bool> UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
            return Task.FromResult(true);
        }

        public Task<bool> ForceEviction(long bytesToFree)
        {
            // Placeholder implementation
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["items_tracked"] = _accessStats.Count,
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }
    }

    // CacheAccessStats is now defined directly in FileBasedCacheProvider
}