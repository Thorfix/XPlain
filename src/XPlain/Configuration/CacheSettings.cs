using System.IO.Compression;
using System.Collections.Generic;

namespace XPlain.Configuration
{
    /// <summary>
    /// Supported compression algorithms
    /// </summary>
    public enum CompressionAlgorithm
    {
        None,
        GZip,
        Brotli
    }
    
    /// <summary>
    /// Content types with specific compression characteristics
    /// </summary>
    public enum ContentType
    {
        Unknown,
        TextJson,
        TextXml,
        TextHtml,
        TextPlain,
        Image,
        Video,
        Audio,
        BinaryData,
        CompressedData
    }

    public class CacheSettings
    {
        public bool CacheEnabled { get; set; } = true;
        public string CacheDirectory { get; set; } = "cache";
        public bool EncryptionEnabled { get; set; } = false;
        public string EncryptionAlgorithm { get; set; } = "AES";
        public string EncryptionKey { get; set; } = null;
        public int EncryptionKeySize { get; set; } = 256;
        public bool AllowUnencryptedLegacyFiles { get; set; } = false;
        public string CodebasePath { get; set; } = "./";
        public int CacheExpirationHours { get; set; } = 24;
        public int MaxCacheSizeMB { get; set; } = 1024;
        public int CleanupIntervalMinutes { get; set; } = 60;
        public bool BackupBeforeModify { get; set; } = true;
        public int MaxBackupFiles { get; set; } = 5;
        public string DefaultEvictionPolicy { get; set; } = "LRU";
        public string[] FrequentQuestions { get; set; } = new string[0];
        
        // Compression settings
        public bool CompressionEnabled { get; set; } = true;
        public CompressionAlgorithm CompressionAlgorithm { get; set; } = CompressionAlgorithm.GZip;
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
        public int MinSizeForCompressionBytes { get; set; } = 1024; // Only compress entries larger than 1KB
        public bool AdaptiveCompression { get; set; } = true; // Auto-tune compression based on content type and size
        public bool UpgradeUncompressedEntries { get; set; } = true; // Auto-upgrade legacy entries to compressed format
        
        // Advanced compression settings
        public Dictionary<ContentType, CompressionAlgorithm> ContentTypeAlgorithmMap { get; set; } = new Dictionary<ContentType, CompressionAlgorithm> {
            { ContentType.TextJson, CompressionAlgorithm.Brotli },
            { ContentType.TextXml, CompressionAlgorithm.Brotli },
            { ContentType.TextHtml, CompressionAlgorithm.Brotli },
            { ContentType.TextPlain, CompressionAlgorithm.GZip },
            { ContentType.Image, CompressionAlgorithm.None },
            { ContentType.Video, CompressionAlgorithm.None },
            { ContentType.Audio, CompressionAlgorithm.None },
            { ContentType.BinaryData, CompressionAlgorithm.GZip },
            { ContentType.CompressedData, CompressionAlgorithm.None }
        };
        
        public Dictionary<ContentType, CompressionLevel> ContentTypeCompressionLevelMap { get; set; } = new Dictionary<ContentType, CompressionLevel> {
            { ContentType.TextJson, CompressionLevel.SmallestSize },
            { ContentType.TextXml, CompressionLevel.SmallestSize },
            { ContentType.TextHtml, CompressionLevel.SmallestSize },
            { ContentType.TextPlain, CompressionLevel.Optimal },
            { ContentType.BinaryData, CompressionLevel.Fastest }
        };
        
        public int MaxSizeForHighCompressionBytes { get; set; } = 10 * 1024 * 1024; // Use highest compression only for files under 10MB
        public bool AutoAdjustCompressionLevel { get; set; } = true; // Automatically adjust compression level based on system load
        public bool TrackCompressionMetrics { get; set; } = true; // Keep detailed metrics about compression performance
        public int CompressionMetricsRetentionHours { get; set; } = 24; // How long to keep detailed compression metrics 
        public double MinCompressionRatio { get; set; } = 0.9; // Only store compressed data if size is reduced by at least 10%
        public bool CompressMetadata { get; set; } = false; // Whether to compress cache metadata in addition to values
    }
}