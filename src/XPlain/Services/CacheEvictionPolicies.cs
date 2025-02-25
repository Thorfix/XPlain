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

    public class DummyEvictionPolicy : ICacheEvictionPolicy
    {
        public double CurrentEvictionThreshold => 0.9;
        public double CurrentPressureThreshold => 0.7;

        public Task<bool> UpdateEvictionThreshold(double threshold)
        {
            return Task.FromResult(true);
        }

        public Task<bool> UpdatePressureThreshold(double threshold)
        {
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

    public class CacheEvictionEvent
    {
        public string Key { get; set; }
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; }
        public long SizeFreed { get; set; }
        public string Strategy { get; set; }
    }

    public class PolicySwitchEvent
    {
        public EvictionStrategy FromStrategy { get; set; }
        public EvictionStrategy ToStrategy { get; set; }
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; }
        public Dictionary<string, double> Metrics { get; set; }
    }

    public class LRUEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9; // 90% capacity
        private double _pressureThreshold = 0.95; // 95% capacity
        private EvictionStrategy _currentStrategy = EvictionStrategy.LRU;

        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public async Task UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
        }

        public async Task UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
        }

        public async Task UpdateEvictionStrategy(EvictionStrategy strategy)
        {
            _currentStrategy = strategy;
        }

        public async Task ForceEviction(long bytesToFree)
        {
            // Implementation handled by cache provider
        }

        public void RecordAccess(string key)
        {
            _lastAccessTimes.AddOrUpdate(key, DateTime.UtcNow, (_, __) => DateTime.UtcNow);
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
    }

    public class HitRateWeightedPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, (int hits, int accesses)> _hitRateStats = new();
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.95;
        private EvictionStrategy _currentStrategy = EvictionStrategy.HitRateWeighted;
        private const int MINIMUM_ACCESSES = 5;

        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public async Task UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
        }

        public async Task UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
        }

        public async Task UpdateEvictionStrategy(EvictionStrategy strategy)
        {
            _currentStrategy = strategy;
        }

        public async Task ForceEviction(long bytesToFree)
        {
            // Implementation handled by cache provider
        }

        private double CalculateHitRate(string key)
        {
            if (_hitRateStats.TryGetValue(key, out var stats))
            {
                if (stats.accesses < MINIMUM_ACCESSES) return 0.5; // Default for new items
                return (double)stats.hits / stats.accesses;
            }
            return 0;
        }

        public void UpdateHitStats(string key, bool isHit)
        {
            _hitRateStats.AddOrUpdate(
                key,
                new (isHit ? 1 : 0, 1),
                (_, old) => (old.hits + (isHit ? 1 : 0), old.accesses + 1)
            );
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["total_items_tracked"] = _hitRateStats.Count,
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }
    }

    public class SizeWeightedPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, (long size, DateTime lastAccess)> _sizeStats = new();
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.95;
        private EvictionStrategy _currentStrategy = EvictionStrategy.SizeWeighted;
        private const long SIZE_THRESHOLD = 1024 * 1024; // 1MB

        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        public async Task UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
        }

        public async Task UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
        }

        public async Task UpdateEvictionStrategy(EvictionStrategy strategy)
        {
            _currentStrategy = strategy;
        }

        public async Task ForceEviction(long bytesToFree)
        {
            // Implementation handled by cache provider
        }

        public void UpdateSizeStats(string key, long size)
        {
            _sizeStats.AddOrUpdate(
                key,
                (size, DateTime.UtcNow),
                (_, old) => (size, DateTime.UtcNow)
            );
        }

        private double CalculateSizeScore(string key)
        {
            if (_sizeStats.TryGetValue(key, out var stats))
            {
                // Larger items get higher scores (more likely to be evicted)
                var sizeScore = (double)stats.size / SIZE_THRESHOLD;
                var ageHours = (DateTime.UtcNow - stats.lastAccess).TotalHours;
                return sizeScore * (1 + (ageHours / 24)); // Size weighted by age
            }
            return 0;
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["items_tracked"] = _sizeStats.Count,
                ["large_items"] = _sizeStats.Count(x => x.Value.size > SIZE_THRESHOLD),
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }
    }

    public class AdaptiveEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly ConcurrentDictionary<string, PolicyState> _policyStates = new();
        private readonly ConcurrentQueue<CacheEvictionEvent> _recentEvictions = new();
        private readonly ConcurrentQueue<PolicySwitchEvent> _switchHistory = new();
        private readonly ConcurrentDictionary<string, MLPredictionMetrics> _mlMetrics = new();
        private EvictionStrategy _currentStrategy = EvictionStrategy.Adaptive;
        private double _evictionThreshold = 0.9;
        private double _pressureThreshold = 0.95;
        private DateTime _lastEvaluation = DateTime.UtcNow;
        private readonly TimeSpan _evaluationInterval = TimeSpan.FromMinutes(5);

        public double CurrentEvictionThreshold => _evictionThreshold;
        public double CurrentPressureThreshold => _pressureThreshold;

        private class PolicyState
        {
            public double HitRate { get; set; }
            public double MemoryEfficiency { get; set; }
            public double ResponseTime { get; set; }
            public DateTime LastUsed { get; set; }
            public int EvictionCount { get; set; }
            public Dictionary<string, double> Metrics { get; set; } = new();
        }

        private class MLPredictionMetrics
        {
            public double PredictedHitRate { get; set; }
            public double PredictedMemoryUsage { get; set; }
            public double PredictedLatency { get; set; }
            public DateTime PredictionTime { get; set; }
        }

        public async Task UpdateEvictionThreshold(double threshold)
        {
            _evictionThreshold = Math.Clamp(threshold, 0.5, 0.95);
        }

        public async Task UpdatePressureThreshold(double threshold)
        {
            _pressureThreshold = Math.Clamp(threshold, 0.6, 0.99);
        }

        public async Task UpdateEvictionStrategy(EvictionStrategy strategy)
        {
            if (strategy != _currentStrategy)
            {
                var switchEvent = new PolicySwitchEvent
                {
                    FromStrategy = _currentStrategy,
                    ToStrategy = strategy,
                    Timestamp = DateTime.UtcNow,
                    Reason = "Manual strategy switch",
                    Metrics = GetCurrentMetrics()
                };

                _switchHistory.Enqueue(switchEvent);
                _currentStrategy = strategy;
            }
        }

        public async Task ForceEviction(long bytesToFree)
        {
            // Implementation handled by cache provider
        }

        private Dictionary<string, double> GetCurrentMetrics()
        {
            if (_policyStates.TryGetValue(_currentStrategy.ToString(), out var state))
            {
                return new Dictionary<string, double>
                {
                    ["hit_rate"] = state.HitRate,
                    ["memory_efficiency"] = state.MemoryEfficiency,
                    ["response_time"] = state.ResponseTime
                };
            }
            return new Dictionary<string, double>();
        }

        public void UpdateMLPredictions(string strategyKey, MLPredictionMetrics metrics)
        {
            _mlMetrics.AddOrUpdate(strategyKey, metrics, (_, __) => metrics);
        }

        public Dictionary<string, int> GetEvictionStats()
        {
            return new Dictionary<string, int>
            {
                ["policy_switches"] = _switchHistory.Count,
                ["tracked_strategies"] = _policyStates.Count,
                ["recent_evictions"] = _recentEvictions.Count
            };
        }

        public IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count)
        {
            return _recentEvictions.Take(count);
        }

        private async Task EvaluateAndSwitchPolicy()
        {
            if (DateTime.UtcNow - _lastEvaluation < _evaluationInterval)
                return;

            _lastEvaluation = DateTime.UtcNow;

            var bestStrategy = await SelectBestStrategy();
            if (bestStrategy != _currentStrategy)
            {
                await UpdateEvictionStrategy(bestStrategy);
            }
        }

        private async Task<EvictionStrategy> SelectBestStrategy()
        {
            var scores = new Dictionary<EvictionStrategy, double>();

            foreach (var strategy in Enum.GetValues<EvictionStrategy>())
            {
                scores[strategy] = await CalculateStrategyScore(strategy);
            }

            return scores.OrderByDescending(s => s.Value).First().Key;
        }

        private async Task<double> CalculateStrategyScore(EvictionStrategy strategy)
        {
            if (!_policyStates.TryGetValue(strategy.ToString(), out var state))
                return 0;

            if (!_mlMetrics.TryGetValue(strategy.ToString(), out var predictions))
                return 0;

            const double currentWeight = 0.6;
            const double predictionWeight = 0.4;

            var currentScore = (state.HitRate * 0.4) +
                             (state.MemoryEfficiency * 0.3) +
                             (1.0 / (state.ResponseTime + 1) * 0.3);

            var predictionScore = (predictions.PredictedHitRate * 0.4) +
                                (1.0 / predictions.PredictedMemoryUsage * 0.3) +
                                (1.0 / predictions.PredictedLatency * 0.3);

            return (currentScore * currentWeight) + (predictionScore * predictionWeight);
        }
    }
}