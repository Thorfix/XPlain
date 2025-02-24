using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheEvictionPolicy
    {
        double CurrentEvictionThreshold { get; }
        double CurrentPressureThreshold { get; }
        
        Task<bool> UpdateEvictionThreshold(double threshold);
        Task<bool> UpdatePressureThreshold(double threshold);
        Task<bool> ForceEviction(long bytesToFree);
        Dictionary<string, int> GetEvictionStats();
        IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count);
    }

    public enum EvictionStrategy
    {
        LRU,
        LFU,
        HitRateWeighted,
        SizeWeighted,
        Adaptive
    }
}