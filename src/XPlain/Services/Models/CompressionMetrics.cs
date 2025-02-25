using System;
using System.Collections.Generic;

namespace XPlain.Services
{
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
}