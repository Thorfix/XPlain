using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class CacheEvictionEvent
    {
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Key { get; set; }
        public long BytesFreed { get; set; }
    }
    
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

    public class CacheEvictionEvent
    {
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}