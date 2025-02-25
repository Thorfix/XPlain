using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class LRUEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly List<CacheEvictionEvent> _recentEvictions = new();
        private readonly Dictionary<string, int> _evictionStats = new()
        {
            ["lru_evictions"] = 0,
            ["forced_evictions"] = 0,
            ["total_evictions"] = 0
        };
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
            _evictionStats["forced_evictions"]++;
            _evictionStats["total_evictions"]++;
            
            // Record eviction event
            _recentEvictions.Add(new CacheEvictionEvent
            {
                Reason = $"Forced eviction of {bytesToFree} bytes",
                Timestamp = DateTime.UtcNow
            });
            
            // Keep only the most recent events
            if (_recentEvictions.Count > 100)
            {
                _recentEvictions.RemoveRange(0, _recentEvictions.Count - 100);
            }
            
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>(_evictionStats);
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.OrderByDescending(e => e.Timestamp).Take(count);
        }
    }
    
    public class HitRateWeightedEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, CacheAccessStats> _accessStats;
        private readonly List<CacheEvictionEvent> _recentEvictions = new();
        private readonly Dictionary<string, int> _evictionStats = new()
        {
            ["weighted_evictions"] = 0,
            ["forced_evictions"] = 0,
            ["total_evictions"] = 0
        };
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.7;
        
        public HitRateWeightedEvictionPolicy(Dictionary<string, CacheAccessStats> accessStats)
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
            _evictionStats["forced_evictions"]++;
            _evictionStats["total_evictions"]++;
            
            // Record eviction event
            _recentEvictions.Add(new CacheEvictionEvent
            {
                Reason = $"Forced eviction of {bytesToFree} bytes",
                Timestamp = DateTime.UtcNow
            });
            
            // Keep only the most recent events
            if (_recentEvictions.Count > 100)
            {
                _recentEvictions.RemoveRange(0, _recentEvictions.Count - 100);
            }
            
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>(_evictionStats);
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.OrderByDescending(e => e.Timestamp).Take(count);
        }
    }
    
    public class SizeWeightedEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly List<CacheEvictionEvent> _recentEvictions = new();
        private readonly Dictionary<string, int> _evictionStats = new()
        {
            ["size_weighted_evictions"] = 0,
            ["forced_evictions"] = 0,
            ["total_evictions"] = 0
        };
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
            _evictionStats["forced_evictions"]++;
            _evictionStats["total_evictions"]++;
            
            // Record eviction event
            _recentEvictions.Add(new CacheEvictionEvent
            {
                Reason = $"Forced eviction of {bytesToFree} bytes",
                Timestamp = DateTime.UtcNow
            });
            
            // Keep only the most recent events
            if (_recentEvictions.Count > 100)
            {
                _recentEvictions.RemoveRange(0, _recentEvictions.Count - 100);
            }
            
            return Task.FromResult(true);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>(_evictionStats);
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.OrderByDescending(e => e.Timestamp).Take(count);
        }
    }
}