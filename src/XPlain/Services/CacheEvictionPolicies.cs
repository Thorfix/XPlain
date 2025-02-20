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

    public class AdaptiveCacheEvictionPolicy : ICacheEvictionPolicy
    {
        private readonly Dictionary<string, ICacheEvictionPolicy> _policies;
        private readonly Dictionary<string, IEvictionPolicyMetrics> _policyMetrics;
        private readonly List<PolicySwitchEvent> _switchHistory;
        private ICacheEvictionPolicy _currentPolicy;
        private DateTime _lastEvaluation;
        private readonly TimeSpan _evaluationInterval = TimeSpan.FromMinutes(5);

        public AdaptiveCacheEvictionPolicy()
        {
            _policies = new Dictionary<string, ICacheEvictionPolicy>
            {
                ["LRU"] = new LRUEvictionPolicy(),
                ["LFU"] = new LFUEvictionPolicy(),
                ["FIFO"] = new FIFOEvictionPolicy(),
                ["SizeWeighted"] = new SizeWeightedEvictionPolicy(),
                ["Hybrid"] = new HybridEvictionPolicy()
            };

            _policyMetrics = new Dictionary<string, IEvictionPolicyMetrics>();
            _switchHistory = new List<PolicySwitchEvent>();
            _currentPolicy = _policies["Hybrid"]; // Start with Hybrid as default
            _lastEvaluation = DateTime.UtcNow;

            InitializeMetrics();
        }

        private void InitializeMetrics()
        {
            foreach (var policy in _policies.Keys)
            {
                _policyMetrics[policy] = new EvictionPolicyMetrics();
            }
        }

        public IEnumerable<string> SelectItemsForEviction(
            IEnumerable<KeyValuePair<string, CacheItemStats>> items, 
            long targetSize)
        {
            EvaluateAndSwitchPolicy();
            return _currentPolicy.SelectItemsForEviction(items, targetSize);
        }

        public void UpdatePolicy(CacheAccessStats stats)
        {
            // Update metrics for all policies
            foreach (var policy in _policies.Values)
            {
                policy.UpdatePolicy(stats);
            }

            // Update policy metrics
            UpdatePolicyMetrics(stats);

            // Learn from performance data
            LearnFromPerformanceData(stats);
        }

        private void EvaluateAndSwitchPolicy()
        {
            if (DateTime.UtcNow - _lastEvaluation < _evaluationInterval)
                return;

            _lastEvaluation = DateTime.UtcNow;

            var bestPolicy = SelectBestPolicy();
            if (bestPolicy.Key != GetCurrentPolicyName())
            {
                SwitchPolicy(bestPolicy.Key, bestPolicy.Value);
            }
        }

        private KeyValuePair<string, double> SelectBestPolicy()
        {
            var scores = new Dictionary<string, double>();

            foreach (var policy in _policies)
            {
                var metrics = _policyMetrics[policy.Key];
                scores[policy.Key] = CalculatePolicyScore(metrics);
            }

            return scores.OrderByDescending(s => s.Value).First();
        }

        private double CalculatePolicyScore(IEvictionPolicyMetrics metrics)
        {
            const double hitRateWeight = 0.35;
            const double memoryWeight = 0.25;
            const double responseTimeWeight = 0.20;
            const double utilizationWeight = 0.10;
            const double accuracyWeight = 0.10;

            return (metrics.HitRate * hitRateWeight) +
                   (metrics.MemoryEfficiency * memoryWeight) +
                   (1 / metrics.AverageResponseTime * responseTimeWeight) +
                   (metrics.ResourceUtilization * utilizationWeight) +
                   (metrics.EvictionAccuracy * accuracyWeight);
        }

        private void SwitchPolicy(string newPolicyName, double score)
        {
            var oldPolicyName = GetCurrentPolicyName();
            var oldPolicy = _currentPolicy;
            _currentPolicy = _policies[newPolicyName];

            var switchEvent = new PolicySwitchEvent
            {
                FromPolicy = oldPolicyName,
                ToPolicy = newPolicyName,
                Timestamp = DateTime.UtcNow,
                PerformanceImpact = new Dictionary<string, double>
                {
                    ["score_improvement"] = score - CalculatePolicyScore(_policyMetrics[oldPolicyName])
                },
                Reason = $"Better overall performance score: {score:F2}"
            };

            _switchHistory.Add(switchEvent);
        }

        private string GetCurrentPolicyName()
        {
            return _policies.First(p => p.Value == _currentPolicy).Key;
        }

        private void UpdatePolicyMetrics(CacheAccessStats stats)
        {
            var currentMetrics = _policyMetrics[GetCurrentPolicyName()] as EvictionPolicyMetrics;
            if (currentMetrics != null)
            {
                currentMetrics.UpdateMetrics(stats);
            }
        }

        private void LearnFromPerformanceData(CacheAccessStats stats)
        {
            // Analyze workload patterns
            var workloadType = AnalyzeWorkloadPattern(stats);
            var temporalPatterns = AnalyzeTemporalPatterns(stats);
            var sizeDistribution = AnalyzeSizeDistribution(stats);

            // Update policy preferences based on patterns
            AdjustPolicyPreferences(workloadType, temporalPatterns, sizeDistribution);
        }

        private string AnalyzeWorkloadPattern(CacheAccessStats stats)
        {
            if (stats.ReadWriteRatio > 0.8) return "ReadHeavy";
            if (stats.ReadWriteRatio < 0.2) return "WriteHeavy";
            return "Balanced";
        }

        private Dictionary<string, string> AnalyzeTemporalPatterns(CacheAccessStats stats)
        {
            return stats.TemporalPatterns.ToDictionary(
                p => p.Key,
                p => IdentifyPattern(p.Value));
        }

        private Dictionary<string, string> AnalyzeSizeDistribution(CacheAccessStats stats)
        {
            return stats.DataSizeDistribution.ToDictionary(
                p => p.Key,
                p => p.Value > 1024 * 1024 ? "Large" : "Small");
        }

        private string IdentifyPattern(List<DateTime> accessTimes)
        {
            // Simple pattern recognition
            var intervals = accessTimes.Zip(accessTimes.Skip(1), (a, b) => b - a).ToList();
            var avgInterval = intervals.Average(i => i.TotalMinutes);
            var stdDev = Math.Sqrt(intervals.Average(i => Math.Pow(i.TotalMinutes - avgInterval, 2)));

            if (stdDev < avgInterval * 0.1) return "Regular";
            if (stdDev < avgInterval * 0.3) return "Somewhat Regular";
            return "Irregular";
        }

        private void AdjustPolicyPreferences(
            string workloadType,
            Dictionary<string, string> temporalPatterns,
            Dictionary<string, string> sizeDistribution)
        {
            var policyPreferences = new Dictionary<string, double>();

            // Adjust preferences based on workload type
            switch (workloadType)
            {
                case "ReadHeavy":
                    policyPreferences["LRU"] = 1.2;
                    policyPreferences["LFU"] = 1.1;
                    break;
                case "WriteHeavy":
                    policyPreferences["FIFO"] = 1.2;
                    policyPreferences["SizeWeighted"] = 1.1;
                    break;
                case "Balanced":
                    policyPreferences["Hybrid"] = 1.2;
                    break;
            }

            // Consider temporal patterns
            if (temporalPatterns.Values.Any(p => p == "Regular"))
            {
                policyPreferences["LFU"] = (policyPreferences.GetValueOrDefault("LFU", 1.0) * 1.1);
            }

            // Consider size distribution
            if (sizeDistribution.Values.Count(s => s == "Large") > sizeDistribution.Values.Count(s => s == "Small"))
            {
                policyPreferences["SizeWeighted"] = (policyPreferences.GetValueOrDefault("SizeWeighted", 1.0) * 1.1);
            }

            // Apply preferences to metrics
            foreach (var (policy, preference) in policyPreferences)
            {
                if (_policyMetrics.TryGetValue(policy, out var metrics))
                {
                    (metrics as EvictionPolicyMetrics)?.ApplyPreference(preference);
                }
            }
        }

        public Dictionary<string, double> GetPolicyMetrics()
        {
            var metrics = new Dictionary<string, double>();

            // Add current policy metrics
            foreach (var (key, value) in _currentPolicy.GetPolicyMetrics())
            {
                metrics[$"current_{key}"] = value;
            }

            // Add adaptive policy specific metrics
            metrics["policy_switches"] = _switchHistory.Count;
            metrics["time_since_last_switch"] = (DateTime.UtcNow - _switchHistory.LastOrDefault()?.Timestamp ?? DateTime.UtcNow).TotalMinutes;
            metrics["current_policy_score"] = CalculatePolicyScore(_policyMetrics[GetCurrentPolicyName()]);

            return metrics;
        }

        private class EvictionPolicyMetrics : IEvictionPolicyMetrics
        {
            public double HitRate { get; private set; }
            public double MemoryEfficiency { get; private set; }
            public double AverageResponseTime { get; private set; }
            public double ResourceUtilization { get; private set; }
            public double EvictionAccuracy { get; private set; }
            public Dictionary<string, double> CustomMetrics { get; }

            private double _preferenceMultiplier = 1.0;

            public EvictionPolicyMetrics()
            {
                CustomMetrics = new Dictionary<string, double>();
                ResetMetrics();
            }

            public void ResetMetrics()
            {
                HitRate = 0.5;
                MemoryEfficiency = 0.5;
                AverageResponseTime = 100;
                ResourceUtilization = 0.5;
                EvictionAccuracy = 0.5;
            }

            public void UpdateMetrics(CacheAccessStats stats)
            {
                HitRate = stats.TotalHits / (double)(stats.TotalHits + stats.TotalMisses);
                MemoryEfficiency = 1 - stats.MemoryUtilization;
                AverageResponseTime = stats.AverageResponseTimes.Values.Average();
                ResourceUtilization = 1 - (stats.UnnecessaryEvictions / (double)stats.TotalHits);
                EvictionAccuracy = stats.HitRateImpact;

                // Apply preference multiplier
                HitRate *= _preferenceMultiplier;
                MemoryEfficiency *= _preferenceMultiplier;
                ResourceUtilization *= _preferenceMultiplier;
                EvictionAccuracy *= _preferenceMultiplier;
            }

            public void ApplyPreference(double multiplier)
            {
                _preferenceMultiplier = multiplier;
            }
        }
    }
}