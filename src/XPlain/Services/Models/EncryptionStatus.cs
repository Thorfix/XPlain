namespace XPlain.Services
{
    /// <summary>
    /// Represents the current status of encryption for the cache
    /// </summary>
    public class EncryptionStatus
    {
        /// <summary>
        /// Whether encryption is enabled for the cache
        /// </summary>
        public bool Enabled { get; set; }
        
        /// <summary>
        /// The encryption algorithm being used
        /// </summary>
        public string Algorithm { get; set; }
        
        /// <summary>
        /// The key size in bits
        /// </summary>
        public int KeySize { get; set; }
        
        /// <summary>
        /// Number of files currently encrypted
        /// </summary>
        public int EncryptedFileCount { get; set; }
    }
}