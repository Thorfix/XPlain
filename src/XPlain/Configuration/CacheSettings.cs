namespace XPlain.Configuration
{
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
    }
}