using System;
using System.Collections.Concurrent;

namespace XPlain.Services
{
    public class LLMProviderMetrics
    {
        private readonly ConcurrentDictionary<string, ProviderStats> _providerStats = new();

        public void RecordSuccess(string providerName, TimeSpan duration)
        {
            var stats = _providerStats.GetOrAdd(providerName, _ => new ProviderStats());
            stats.RecordSuccess(duration);
        }

        public void RecordFailure(string providerName)
        {
            var stats = _providerStats.GetOrAdd(providerName, _ => new ProviderStats());
            stats.RecordFailure();
        }

        public ProviderStats GetStats(string providerName)
        {
            return _providerStats.GetOrAdd(providerName, _ => new ProviderStats());
        }
    }

    public class ProviderStats
    {
        private long _totalRequests;
        private long _failedRequests;
        private long _totalDurationTicks;
        private readonly object _lock = new();

        public void RecordSuccess(TimeSpan duration)
        {
            lock (_lock)
            {
                _totalRequests++;
                _totalDurationTicks += duration.Ticks;
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                _totalRequests++;
                _failedRequests++;
            }
        }

        public double SuccessRate
        {
            get
            {
                if (_totalRequests == 0) return 1.0;
                return 1.0 - ((double)_failedRequests / _totalRequests);
            }
        }

        public TimeSpan AverageLatency
        {
            get
            {
                if (_totalRequests == 0) return TimeSpan.Zero;
                return TimeSpan.FromTicks(_totalDurationTicks / (_totalRequests - _failedRequests));
            }
        }

        public long TotalRequests => _totalRequests;
        public long FailedRequests => _failedRequests;
    }
}