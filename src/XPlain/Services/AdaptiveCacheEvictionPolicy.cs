using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class AdaptiveCacheEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, CacheAccessStats> _accessStats;
        private readonly List<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.7;
        
        public AdaptiveCacheEvictionPolicy(Dictionary<string, CacheAccessStats> accessStats)
        {
            _accessStats = accessStats ?? new Dictionary<string, CacheAccessStats>();
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
        
        public Dictionary<string, double> GetPolicyMetrics()
        {
            return new Dictionary<string, double>
            {
                ["current_eviction_threshold"] = _evictionThreshold,
                ["current_pressure_threshold"] = _pressureThreshold,
                ["tracked_items"] = _accessStats.Count,
                ["average_access_count"] = _accessStats.Values.Average(s => s.AccessCount)
            };
        }
    }
    
    public class CacheAccessStats
    {
        public long AccessCount { get; set; }
        public long PreWarmCount { get; set; }
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }
}