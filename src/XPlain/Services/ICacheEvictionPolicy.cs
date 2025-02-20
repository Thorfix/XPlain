using System;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheEvictionPolicy
    {
        double CurrentEvictionThreshold { get; }
        double CurrentPressureThreshold { get; }
        
        Task UpdateEvictionThreshold(double threshold);
        Task UpdatePressureThreshold(double threshold);
        Task UpdateEvictionStrategy(EvictionStrategy strategy);
        Task ForceEviction(long bytesToFree);
        Dictionary<string, int> GetEvictionStats();
        IEnumerable<CacheEvictionEvent> GetRecentEvictions(int count);
    }

    public enum EvictionStrategy
    {
        LRU,            // Traditional Least Recently Used
        HitRateWeighted,// Weighted by hit rate patterns
        SizeWeighted,   // Weighted by entry size
        Adaptive        // Dynamically adjusts based on patterns
    }
}