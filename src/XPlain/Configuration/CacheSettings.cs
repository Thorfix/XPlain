namespace XPlain.Configuration
{
    public class CacheSettings
    {
        public bool CacheEnabled { get; set; } = true;
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
    }
}