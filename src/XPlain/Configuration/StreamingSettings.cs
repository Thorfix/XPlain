using System;
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
        
        [Range(200, 20000)]
        public int MaxRetryDelayMs { get; set; } = 10000;
        
        [Range(1.0, 3.0)]
        public double BackoffMultiplier { get; set; } = 2.0;
        
        [Range(0.0, 1.0)]
        public double JitterFactor { get; set; } = 0.1;
        
        public void Validate()
        {
            if (StreamingTimeoutSeconds < 5 || StreamingTimeoutSeconds > 300)
                throw new ValidationException("Streaming timeout must be between 5 and 300 seconds");
                
            if (MaxStreamingRetries < 0 || MaxStreamingRetries > 10)
                throw new ValidationException("Max streaming retries must be between 0 and 10");
                
            if (InitialRetryDelayMs < 100 || InitialRetryDelayMs > 10000)
                throw new ValidationException("Initial retry delay must be between 100 and 10000 milliseconds");
                
            if (MaxRetryDelayMs < InitialRetryDelayMs)
                throw new ValidationException("Maximum retry delay must be greater than or equal to initial retry delay");
                
            if (BackoffMultiplier < 1.0)
                throw new ValidationException("Backoff multiplier must be greater than or equal to 1.0");
                
            if (JitterFactor < 0.0 || JitterFactor > 1.0)
                throw new ValidationException("Jitter factor must be between 0.0 and 1.0");
        }
    }
}