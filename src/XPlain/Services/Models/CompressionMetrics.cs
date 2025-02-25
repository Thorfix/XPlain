using System;

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
        /// Percentage of storage saved by compression
        /// </summary>
        public double CompressionSavingPercent => 
            OriginalSizeBytes > 0 
                ? Math.Round(100.0 * (OriginalSizeBytes - CompressedSizeBytes) / OriginalSizeBytes, 2) 
                : 0;
        
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
    }
}