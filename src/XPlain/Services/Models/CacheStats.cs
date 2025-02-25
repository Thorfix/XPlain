using System;
using System.Collections.Generic;
using XPlain.Configuration;

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
        /// Compression history over time for trend analysis
        /// </summary>
        public List<CompressionHistoryEntry> CompressionHistory { get; set; } = new List<CompressionHistoryEntry>();
        
        /// <summary>
        /// Map of content types to their compression metrics
        /// </summary>
        public Dictionary<ContentType, CompressionMetrics> CompressionByContentType { get; set; } = new Dictionary<ContentType, CompressionMetrics>();
        
        /// <summary>
        /// How many items are actually compressed out of total cached items
        /// </summary>
        public long CompressedItemCount { get; set; }
        
        /// <summary>
        /// Average time in milliseconds spent on compression operations (CPU overhead)
        /// </summary>
        public double AverageCompressionTimeMs { get; set; }
        
        /// <summary>
        /// Average time in milliseconds spent on decompression operations (CPU overhead)
        /// </summary>
        public double AverageDecompressionTimeMs { get; set; }
        
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
                
        /// <summary>
        /// Percentage of cached items that benefit from compression
        /// </summary>
        public double CompressedItemPercentage =>
            CachedItemCount > 0 
                ? 100.0 * CompressedItemCount / CachedItemCount 
                : 0;
                
        /// <summary>
        /// Overall compression efficiency considering both size savings and CPU cost
        /// </summary>
        public double CompressionEfficiencyScore
        {
            get
            {
                if (StorageUsageBytes == 0 || AverageCompressionTimeMs == 0)
                    return 0;
                
                // Calculate efficiency: balance between compression ratio and CPU cost
                double compressionBenefit = 1.0 - CompressionRatio; // Higher is better
                double cpuCost = Math.Min(1.0, AverageCompressionTimeMs / 100.0); // Normalize, lower is better
                
                // Weighted score that prioritizes compression ratio but considers CPU cost
                return (compressionBenefit * 0.8) - (cpuCost * 0.2);
            }
        }
        
        /// <summary>
        /// Best performing compression algorithm based on efficiency score
        /// </summary>
        public string MostEfficientAlgorithm 
        { 
            get
            {
                string bestAlgorithm = "None";
                double bestScore = -1;
                
                foreach (var kvp in CompressionStats)
                {
                    if (kvp.Key != "None" && kvp.Value.EfficiencyScore > bestScore)
                    {
                        bestScore = kvp.Value.EfficiencyScore;
                        bestAlgorithm = kvp.Key;
                    }
                }
                
                return bestAlgorithm;
            }
        }
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
    
    /// <summary>
    /// Represents a point-in-time snapshot of compression statistics for historical tracking
    /// </summary>
    public class CompressionHistoryEntry
    {
        /// <summary>
        /// When this snapshot was taken
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Overall compression ratio at this point in time
        /// </summary>
        public double CompressionRatio { get; set; }
        
        /// <summary>
        /// Total bytes saved by compression at this point
        /// </summary>
        public long BytesSaved { get; set; }
        
        /// <summary>
        /// What percentage of cache items were compressed
        /// </summary>
        public double CompressedItemPercentage { get; set; }
        
        /// <summary>
        /// Average CPU time spent on compression operations (ms)
        /// </summary>
        public double AverageCompressionTimeMs { get; set; }
        
        /// <summary>
        /// Current most efficient algorithm at this point in time
        /// </summary>
        public string PrimaryAlgorithm { get; set; }
        
        /// <summary>
        /// Efficiency score at this point in time
        /// </summary>
        public double EfficiencyScore { get; set; }
    }
}