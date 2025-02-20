namespace XPlain.Configuration
{
    public enum EvictionPolicyType
    {
        LRU,
        LFU,
        FIFO,
        SizeWeighted,
        Hybrid
    }

    public class CacheSettings
    {
        public bool CacheEnabled { get; set; } = true;
        public EvictionPolicyType EvictionPolicy { get; set; } = EvictionPolicyType.LRU;
        public Dictionary<string, object> EvictionPolicyParameters { get; set; } = new();
        public string? CacheDirectory { get; set; }
        public int CacheExpirationHours { get; set; } = 24;
        public string CodebasePath { get; set; } = string.Empty;
        public string[] FrequentQuestions { get; set; } = Array.Empty<string>();

        // Encryption settings
        public bool EncryptionEnabled { get; set; } = false;
        public string? EncryptionKey { get; set; }
        public string EncryptionAlgorithm { get; set; } = "AES-256";
        public string? KeyRotationSchedule { get; set; }
        public bool AllowUnencryptedLegacyFiles { get; set; } = true;

        // Cache Maintenance Settings
        public long MaxCacheSizeBytes { get; set; } = 1024 * 1024 * 1024; // 1GB default
        public double CleanupThresholdPercent { get; set; } = 85; // Cleanup when 85% full
        public int MaintenanceIntervalMinutes { get; set; } = 60; // Run maintenance every hour
        public int KeepRecentItemsHours { get; set; } = 48; // Keep items from last 48 hours
        public Dictionary<string, long> QueryTypeQuotas { get; set; } = new();
        
        // Pre-warming Settings
        public bool EnableCacheWarming { get; set; } = true;
        public int WarmupIntervalMinutes { get; set; } = 240; // Run warmup every 4 hours
        public int MaxWarmupQueries { get; set; } = 50; // Max queries to warm up
        public int MinQueryFrequency { get; set; } = 5; // Min frequency to consider for warmup
        
        // Key Rotation Settings
        public bool EnableKeyRotation { get; set; } = true;
        public int KeyRotationDays { get; set; } = 30; // Rotate keys every 30 days
        
        // Maintenance Window Settings
        public bool RestrictMaintenanceWindow { get; set; } = false;
        public TimeSpan MaintenanceWindowStart { get; set; } = new TimeSpan(2, 0, 0); // 2 AM
        public TimeSpan MaintenanceWindowEnd { get; set; } = new TimeSpan(4, 0, 0); // 4 AM
    }
}