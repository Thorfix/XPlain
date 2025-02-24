using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class LLMProviderMetrics
    {
        private readonly ConcurrentDictionary<string, ProviderStats> _providerStats = new();

        private class ProviderStats
        {
            public long SuccessCount { get; set; }
            public long FailureCount { get; set; }
            public List<double> ResponseTimes { get; } = new List<double>();
            public DateTime LastSuccess { get; set; }
            public DateTime LastFailure { get; set; }
        }

        public void RecordSuccess(string providerName, TimeSpan responseTime)
        {
            var stats = _providerStats.GetOrAdd(providerName, _ => new ProviderStats());
            
            lock (stats)
            {
                stats.SuccessCount++;
                stats.ResponseTimes.Add(responseTime.TotalMilliseconds);
                stats.LastSuccess = DateTime.UtcNow;
                
                // Keep response times history bounded
                if (stats.ResponseTimes.Count > 1000)
                {
                    stats.ResponseTimes.RemoveRange(0, stats.ResponseTimes.Count - 1000);
                }
            }
        }

        public void RecordFailure(string providerName)
        {
            var stats = _providerStats.GetOrAdd(providerName, _ => new ProviderStats());
            
            lock (stats)
            {
                stats.FailureCount++;
                stats.LastFailure = DateTime.UtcNow;
            }
        }

        public Dictionary<string, object> GetMetrics(string providerName)
        {
            if (!_providerStats.TryGetValue(providerName, out var stats))
            {
                return new Dictionary<string, object>
                {
                    ["success_count"] = 0,
                    ["failure_count"] = 0,
                    ["success_rate"] = 0,
                    ["avg_response_time_ms"] = 0,
                    ["last_success"] = null,
                    ["last_failure"] = null
                };
            }

            lock (stats)
            {
                double avgResponseTime = 0;
                if (stats.ResponseTimes.Count > 0)
                {
                    avgResponseTime = stats.ResponseTimes.Average();
                }

                double successRate = 0;
                if (stats.SuccessCount + stats.FailureCount > 0)
                {
                    successRate = (double)stats.SuccessCount / (stats.SuccessCount + stats.FailureCount);
                }

                return new Dictionary<string, object>
                {
                    ["success_count"] = stats.SuccessCount,
                    ["failure_count"] = stats.FailureCount,
                    ["success_rate"] = successRate,
                    ["avg_response_time_ms"] = avgResponseTime,
                    ["last_success"] = stats.LastSuccess,
                    ["last_failure"] = stats.LastFailure
                };
            }
        }

        public Dictionary<string, Dictionary<string, object>> GetAllMetrics()
        {
            var result = new Dictionary<string, Dictionary<string, object>>();
            
            foreach (var provider in _providerStats.Keys)
            {
                result[provider] = GetMetrics(provider);
            }
            
            return result;
        }
    }
}