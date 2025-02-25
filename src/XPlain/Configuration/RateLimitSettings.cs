using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class RateLimitSettings
    {
        [Range(1, 1000)]
        public int RequestsPerWindow { get; set; } = 60;
        
        [Range(1, 3600)]
        public int WindowSeconds { get; set; } = 60;
        
        [Range(1, 100)]
        public int MaxConcurrentRequests { get; set; } = 10;
        
        [Range(0, 10)]
        public int DefaultRetryCount { get; set; } = 3;
        
        [Range(100, 10000)]
        public int InitialRetryDelayMs { get; set; } = 1000;
        
        [Range(1000, 300000)]
        public int MaxRetryDelayMs { get; set; } = 30000;
        
        [Range(1.1, 5.0)]
        public double RetryBackoffMultiplier { get; set; } = 2.0;

        [Range(0.001, 100.0)]
        public decimal CostPerRequest { get; set; } = 0.01m;
        
        [Range(0.01, 1000.0)]
        public decimal DailyCostLimit { get; set; } = 10.0m;
        
        public Dictionary<string, ProviderRateLimitSettings> ProviderSpecificSettings { get; set; } 
            = new Dictionary<string, ProviderRateLimitSettings>();
    }
    
    public class ProviderRateLimitSettings
    {
        public int? RequestsPerWindow { get; set; }
        public int? WindowSeconds { get; set; }
        public int? MaxConcurrentRequests { get; set; }
        public decimal? CostPerRequest { get; set; }
        public decimal? DailyCostLimit { get; set; }
    }
}