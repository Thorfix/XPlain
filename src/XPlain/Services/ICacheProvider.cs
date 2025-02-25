using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XPlain.Configuration;

namespace XPlain.Services
{
    public interface ICacheProvider
    {
        Task<bool> IsKeyFresh(string key);
        Task<bool> PreWarmKey(string key, PreWarmPriority priority = PreWarmPriority.Medium);
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
        Task RemoveAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task WarmupCacheAsync(string[] questions, string codeContext);
        Task InvalidateOnCodeChangeAsync(string codeHash);
        Task<string> GeneratePerformanceChartAsync(OutputFormat format);
        Task<List<string>> GetCacheWarmingRecommendationsAsync();
        Task LogQueryStatsAsync(string queryType, string query, double responseTime, bool hit);
        CacheStats GetCacheStats();
        Task AddEventListener(ICacheEventListener listener);
        Task RemoveEventListener(ICacheEventListener listener);
    }

    public enum PreWarmPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

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
    
    /// <summary>
    /// Stores metrics related to cache compression performance and efficiency
    /// </summary>
    public class CompressionMetrics
    {
        /// <summary>
        /// Total number of items processed by this compression algorithm
        /// </summary>
        public long TotalItems { get; set; }
        
        /// <summary>
        /// Number of items that were successfully compressed (size was reduced)
        /// </summary>
        public long CompressedItems { get; set; }
        
        /// <summary>
        /// Total original size in bytes before compression
        /// </summary>
        public long OriginalSizeBytes { get; set; }
        
        /// <summary>
        /// Total compressed size in bytes after compression
        /// </summary>
        public long CompressedSizeBytes { get; set; }
        
        /// <summary>
        /// Average time in milliseconds to compress an item
        /// </summary>
        public double AverageCompressionTimeMs { get; set; }
        
        /// <summary>
        /// Average time in milliseconds to decompress an item
        /// </summary>
        public double AverageDecompressionTimeMs { get; set; }
        
        /// <summary>
        /// Peak compression time in milliseconds
        /// </summary>
        public double PeakCompressionTimeMs { get; set; }
        
        /// <summary>
        /// Peak decompression time in milliseconds
        /// </summary>
        public double PeakDecompressionTimeMs { get; set; }
        
        /// <summary>
        /// Recent compression times to track performance trends (milliseconds)
        /// </summary>
        public List<double> RecentCompressionTimes { get; set; } = new List<double>();
        
        /// <summary>
        /// Recent decompression times to track performance trends (milliseconds)
        /// </summary>
        public List<double> RecentDecompressionTimes { get; set; } = new List<double>();
        
        /// <summary>
        /// Count of compression operations that exceeded performance thresholds
        /// </summary>
        public int SlowCompressionCount { get; set; }
        
        /// <summary>
        /// Count of decompression operations that exceeded performance thresholds
        /// </summary>
        public int SlowDecompressionCount { get; set; }
        
        /// <summary>
        /// Percentage of storage saved by compression
        /// </summary>
        public double CompressionSavingPercent => 
            OriginalSizeBytes > 0 
                ? Math.Round(100.0 * (OriginalSizeBytes - CompressedSizeBytes) / OriginalSizeBytes, 2) 
                : 0;
        
        /// <summary>
        /// Tracks compression performance by size range (in KB)
        /// </summary>
        public Dictionary<string, PerformanceBySize> PerformanceBySizeRange { get; set; } = new Dictionary<string, PerformanceBySize>();
        
        /// <summary>
        /// Ratio of compressed size to original size (smaller is better)
        /// </summary>
        public double CompressionRatio => 
            OriginalSizeBytes > 0 
                ? Math.Round((double)CompressedSizeBytes / OriginalSizeBytes, 4) 
                : 1.0;
        
        /// <summary>
        /// Total bytes saved through compression
        /// </summary>
        public long BytesSaved => 
            OriginalSizeBytes > CompressedSizeBytes 
                ? OriginalSizeBytes - CompressedSizeBytes 
                : 0;
                
        /// <summary>
        /// Average bytes saved per item
        /// </summary>
        public long AverageBytesPerItemSaved => 
            CompressedItems > 0 
                ? BytesSaved / CompressedItems 
                : 0;
                
        /// <summary>
        /// Efficiency score calculation (higher is better)
        /// Balances compression ratio, time cost, and item success rate
        /// </summary>
        public double EfficiencyScore =>
            TotalItems > 0 && AverageCompressionTimeMs > 0
                ? (1.0 - CompressionRatio) * (CompressedItems / (double)TotalItems) * 
                  (100.0 / Math.Max(1.0, AverageCompressionTimeMs))
                : 0;
                
        /// <summary>
        /// Measures efficiency based on total resource impact (compression time + decompression time + storage)
        /// </summary>
        public double ResourceEfficiencyScore
        {
            get
            {
                if (TotalItems == 0 || OriginalSizeBytes == 0)
                    return 0;
                    
                // Calculate CPU cost (normalized to 0-1 range, lower is better)
                double cpuCost = Math.Min(1.0, (AverageCompressionTimeMs + AverageDecompressionTimeMs) / 200.0);
                
                // Calculate storage savings (0-1 range, higher is better)
                double storageSavings = 1.0 - CompressionRatio;
                
                // Higher score is better - prioritizes storage savings (70%) over CPU cost (30%)
                return (storageSavings * 0.7) - (cpuCost * 0.3);
            }
        }
        
        /// <summary>
        /// Indicates whether the compression algorithm is performing efficiently
        /// </summary>
        public bool IsEfficient => ResourceEfficiencyScore > 0.3 && CompressionRatio < 0.7;
    }
    
    /// <summary>
    /// Tracks compression performance metrics by data size ranges
    /// </summary>
    public class PerformanceBySize
    {
        /// <summary>
        /// Size range this entry represents (e.g. "1KB-10KB")
        /// </summary>
        public string SizeRange { get; set; }
        
        /// <summary>
        /// Minimum size in bytes for this range
        /// </summary>
        public long MinSizeBytes { get; set; }
        
        /// <summary>
        /// Maximum size in bytes for this range
        /// </summary>
        public long MaxSizeBytes { get; set; }
        
        /// <summary>
        /// Total items processed in this size range
        /// </summary>
        public int ItemCount { get; set; }
        
        /// <summary>
        /// Average compression ratio for this size range
        /// </summary>
        public double AverageCompressionRatio { get; set; }
        
        /// <summary>
        /// Average compression time (ms) for this size range
        /// </summary>
        public double AverageCompressionTimeMs { get; set; }
        
        /// <summary>
        /// Average decompression time (ms) for this size range
        /// </summary>
        public double AverageDecompressionTimeMs { get; set; }
        
        /// <summary>
        /// Efficiency score for this size range
        /// </summary>
        public double EfficiencyScore => 
            ItemCount > 0 && AverageCompressionTimeMs > 0
                ? (1.0 - AverageCompressionRatio) * (100.0 / Math.Max(1.0, AverageCompressionTimeMs))
                : 0;
    }
    
    public class EncryptionStatus
    {
        public bool Enabled { get; set; }
        public string Algorithm { get; set; }
        public long EncryptedFileCount { get; set; }
    }

    public class CachePerformanceMetrics
    {
        public double CachedResponseTime { get; set; }
        public double NonCachedResponseTime { get; set; }
        public double PerformanceGain => NonCachedResponseTime > 0 
            ? ((NonCachedResponseTime - CachedResponseTime) / NonCachedResponseTime) * 100 
            : 0;
    }

    public class CacheInvalidationEvent
    {
        public string Reason { get; set; }
        public DateTime Time { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; } = new();
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