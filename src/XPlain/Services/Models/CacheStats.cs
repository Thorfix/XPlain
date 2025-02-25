using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class CacheStats
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long CachedItemCount { get; set; }
        public long StorageUsageBytes { get; set; }
        public long CompressedStorageUsageBytes { get; set; }
        public Dictionary<string, long> QueryTypeStats { get; set; } = new Dictionary<string, long>();
        public Dictionary<string, double> AverageResponseTimes { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, CachePerformanceMetrics> PerformanceByQueryType { get; set; } = new Dictionary<string, CachePerformanceMetrics>();
        public List<CacheInvalidationEvent> InvalidationHistory { get; set; } = new List<CacheInvalidationEvent>();
        public long InvalidationCount { get; set; }
        public Dictionary<string, int> TopQueries { get; set; } = new Dictionary<string, int>();
        public DateTime LastStatsUpdate { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Statistics about compression performance for each algorithm
        /// </summary>
        public Dictionary<string, CompressionMetrics> CompressionStats { get; set; } = new Dictionary<string, CompressionMetrics>();
        
        /// <summary>
        /// Information about the encryption status of the cache
        /// </summary>
        public EncryptionStatus EncryptionStatus { get; set; } = new EncryptionStatus();
        
        /// <summary>
        /// The overall ratio of hits to total cache accesses
        /// </summary>
        public double HitRatio => (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) : 0;
        
        /// <summary>
        /// The overall compression ratio (compressed size / original size)
        /// </summary>
        public double CompressionRatio => 
            StorageUsageBytes > 0 
                ? (double)CompressedStorageUsageBytes / StorageUsageBytes 
                : 1.0;
        
        /// <summary>
        /// Total bytes saved by compression
        /// </summary>
        public long CompressionSavingsBytes => 
            StorageUsageBytes > CompressedStorageUsageBytes 
                ? StorageUsageBytes - CompressedStorageUsageBytes 
                : 0;
        
        /// <summary>
        /// Percentage of storage saved by compression
        /// </summary>
        public double CompressionSavingsPercent => 
            StorageUsageBytes > 0 
                ? 100.0 * (StorageUsageBytes - CompressedStorageUsageBytes) / StorageUsageBytes 
                : 0;
    }

    public class CachePerformanceMetrics
    {
        public double PerformanceGain { get; set; }
        public double CachedResponseTime { get; set; }
        public double NonCachedResponseTime { get; set; }
    }

    public class CacheInvalidationEvent
    {
        public string Reason { get; set; }
        public DateTime Time { get; set; } = DateTime.UtcNow;
    }
}