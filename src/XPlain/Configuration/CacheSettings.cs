using System;
using System.IO;

namespace XPlain.Configuration
{
    public class CacheSettings
    {
        /// <summary>
        /// Whether caching is enabled
        /// </summary>
        public bool CacheEnabled { get; set; } = true;

        /// <summary>
        /// Directory to store cache files
        /// </summary>
        public string CacheDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XPlain", "Cache");

        /// <summary>
        /// Whether file encryption is enabled for sensitive data
        /// </summary>
        public bool EncryptionEnabled { get; set; } = true;

        /// <summary>
        /// Encryption algorithm to use
        /// </summary>
        public string EncryptionAlgorithm { get; set; } = "AES";

        /// <summary>
        /// Encryption key size in bits
        /// </summary>
        public int EncryptionKeySize { get; set; } = 256;

        /// <summary>
        /// Base64-encoded encryption key (if not provided, a random key will be generated)
        /// </summary>
        public string EncryptionKey { get; set; }

        /// <summary>
        /// Default expiration time for cache entries in hours
        /// </summary>
        public int CacheExpirationHours { get; set; } = 24;

        /// <summary>
        /// Whether to allow access to unencrypted legacy cache files
        /// </summary>
        public bool AllowUnencryptedLegacyFiles { get; set; } = false;

        /// <summary>
        /// Maximum size of the cache in MB (0 for unlimited)
        /// </summary>
        public int MaxCacheSizeMB { get; set; } = 1024;

        /// <summary>
        /// Path to the code directory
        /// </summary>
        public string CodebasePath { get; set; } = ".";

        /// <summary>
        /// Common questions to pre-warm in the cache
        /// </summary>
        public string[] FrequentQuestions { get; set; } = Array.Empty<string>();

        /// <summary>
        /// How often to clean up expired cache entries (in minutes)
        /// </summary>
        public int CleanupIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// Whether to backup cache entries before modifying them
        /// </summary>
        public bool BackupBeforeModify { get; set; } = true;

        /// <summary>
        /// Maximum number of backup files to keep
        /// </summary>
        public int MaxBackupFiles { get; set; } = 5;

        /// <summary>
        /// Default cache eviction policy
        /// </summary>
        public string DefaultEvictionPolicy { get; set; } = "LRU";
    }
}