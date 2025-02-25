using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class StreamingSettings
    {
        [Required]
        public bool EnableStreamingByDefault { get; set; }
        
        [Range(5, 300)]
        public int StreamingTimeoutSeconds { get; set; } = 30;
        
        [Range(0, 10)]
        public int MaxStreamingRetries { get; set; } = 3;
        
        [Range(100, 10000)]
        public int InitialRetryDelayMs { get; set; } = 1000;
    }
}