using System.IO.Compression;

namespace XPlain.Configuration
{
    public enum CompressionAlgorithm
    {
        None,
        GZip,
        Brotli
    }

    public class CacheSettings
    {
        public bool CacheEnabled { get; set; } = true;
        public string CacheDirectory { get; set; } = "cache";
        public bool EncryptionEnabled { get; set; } = false;
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
    }
}