using System;
using System.Collections.Generic;
using System.Linq;

namespace XPlain.Services
{
    public class LRUEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, DateTime> _lastAccessTimes = new();
        
        public IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize)
        {
            var currentSize = items.Sum(i => i.Value.Size);
            var itemList = items.OrderBy(i => i.Value.LastAccess).ToList();
            var result = new List<string>();
            
            foreach (var item in itemList)
            {
                if (currentSize <= targetSize) break;
                result.Add(item.Key);
                currentSize -= item.Value.Size;
            }
            
            return result;
        }

        public void UpdatePolicy(CacheAccessStats stats)
        {
            // LRU policy doesn't need to track additional statistics
        }

        public Dictionary<string, double> GetPolicyMetrics()
        {
            return new Dictionary<string, double>
            {
                ["average_item_age_hours"] = _lastAccessTimes.Any() 
                    ? _lastAccessTimes.Values.Average(t => (DateTime.UtcNow - t).TotalHours)
                    : 0
            };
        }
    }

    public class LFUEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, int> _accessCounts = new();
        
        public IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize)
        {
            var currentSize = items.Sum(i => i.Value.Size);
            var itemList = items.OrderBy(i => i.Value.AccessCount).ToList();
            var result = new List<string>();
            
            foreach (var item in itemList)
            {
                if (currentSize <= targetSize) break;
                result.Add(item.Key);
                currentSize -= item.Value.Size;
            }
            
            return result;
        }

        public void UpdatePolicy(CacheAccessStats stats)
        {
            // Update access frequencies
            foreach (var freq in stats.QueryTypeFrequency)
            {
                if (_accessCounts.ContainsKey(freq.Key))
                    _accessCounts[freq.Key] += freq.Value;
                else
                    _accessCounts[freq.Key] = freq.Value;
            }
        }

        public Dictionary<string, double> GetPolicyMetrics()
        {
            return new Dictionary<string, double>
            {
                ["average_access_frequency"] = _accessCounts.Any() 
                    ? _accessCounts.Values.Average() 
                    : 0
            };
        }
    }

    public class FIFOEvictionPolicy : ICacheEvictionPolicy
    {
        public IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize)
        {
            var currentSize = items.Sum(i => i.Value.Size);
            var itemList = items.OrderBy(i => i.Value.CreationTime).ToList();
            var result = new List<string>();
            
            foreach (var item in itemList)
            {
                if (currentSize <= targetSize) break;
                result.Add(item.Key);
                currentSize -= item.Value.Size;
            }
            
            return result;
        }

        public void UpdatePolicy(CacheAccessStats stats)
        {
            // FIFO policy doesn't need to track additional statistics
        }

        public Dictionary<string, double> GetPolicyMetrics()
        {
            return new Dictionary<string, double>(); // No specific metrics for FIFO
        }
    }

    public class SizeWeightedEvictionPolicy : ICacheEvictionPolicy
    {
        public IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize)
        {
            var currentSize = items.Sum(i => i.Value.Size);
            // Prioritize larger items for eviction
            var itemList = items.OrderByDescending(i => i.Value.Size).ToList();
            var result = new List<string>();
            
            foreach (var item in itemList)
            {
                if (currentSize <= targetSize) break;
                result.Add(item.Key);
                currentSize -= item.Value.Size;
            }
            
            return result;
        }

        public void UpdatePolicy(CacheAccessStats stats)
        {
            // Size-weighted policy doesn't need to track additional statistics
        }

        public Dictionary<string, double> GetPolicyMetrics()
        {
            return new Dictionary<string, double>(); // No specific metrics for size-weighted
        }
    }

    public class HybridEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, HybridStats> _itemStats = new();
        
        public IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize)
        {
            var currentSize = items.Sum(i => i.Value.Size);
            var itemList = items.ToList();
            
            // Calculate score for each item (lower is better)
            var scores = itemList.Select(item => new
            {
                Key = item.Key,
                Score = CalculateScore(item.Value),
                Size = item.Value.Size
            }).OrderByDescending(x => x.Score).ToList();
            
            var result = new List<string>();
            foreach (var item in scores)
            {
                if (currentSize <= targetSize) break;
                result.Add(item.Key);
                currentSize -= item.Size;
            }
            
            return result;
        }

        private double CalculateScore(CacheItemStats stats)
        {
            const double sizeWeight = 0.3;
            const double ageWeight = 0.3;
            const double frequencyWeight = 0.4;
            
            var ageHours = (DateTime.UtcNow - stats.LastAccess).TotalHours;
            var sizeScore = stats.Size / (1024.0 * 1024.0); // Size in MB
            var ageScore = ageHours / 24.0; // Age in days
            var frequencyScore = stats.AccessCount;
            
            return (sizeScore * sizeWeight) + 
                   (ageScore * ageWeight) - 
                   (frequencyScore * frequencyWeight);
        }

        public void UpdatePolicy(CacheAccessStats stats)
        {
            foreach (var freq in stats.QueryTypeFrequency)
            {
                if (!_itemStats.ContainsKey(freq.Key))
                {
                    _itemStats[freq.Key] = new HybridStats();
                }
                _itemStats[freq.Key].AccessCount += freq.Value;
                _itemStats[freq.Key].LastAccess = DateTime.UtcNow;
            }
        }

        public Dictionary<string, double> GetPolicyMetrics()
        {
            var avgScore = _itemStats.Any()
                ? _itemStats.Average(s => CalculateScore(new CacheItemStats
                {
                    Size = s.Value.Size,
                    LastAccess = s.Value.LastAccess,
                    AccessCount = s.Value.AccessCount,
                    CreationTime = s.Value.CreationTime
                }))
                : 0;

            return new Dictionary<string, double>
            {
                ["average_hybrid_score"] = avgScore
            };
        }

        private class HybridStats
        {
            public long Size { get; set; }
            public DateTime LastAccess { get; set; } = DateTime.UtcNow;
            public DateTime CreationTime { get; set; } = DateTime.UtcNow;
            public int AccessCount { get; set; }
        }
    }
}