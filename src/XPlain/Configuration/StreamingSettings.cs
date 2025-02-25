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
        
        public void Validate()
        {
            if (StreamingTimeoutSeconds < 5 || StreamingTimeoutSeconds > 300)
                throw new ValidationException("Streaming timeout must be between 5 and 300 seconds");
                
            if (MaxStreamingRetries < 0 || MaxStreamingRetries > 10)
                throw new ValidationException("Max streaming retries must be between 0 and 10");
                
            if (InitialRetryDelayMs < 100 || InitialRetryDelayMs > 10000)
                throw new ValidationException("Initial retry delay must be between 100 and 10000 milliseconds");
        }
    }
}