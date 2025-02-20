namespace XPlain.Configuration
{
    public class RateLimitSettings
    {
        public int RequestsPerWindow { get; set; } = 50;
        public int WindowSeconds { get; set; } = 60;
        public int MaxConcurrentRequests { get; set; } = 5;
        public int DefaultRetryCount { get; set; } = 3;
        public int InitialRetryDelayMs { get; set; } = 1000;
        public double MaxRetryDelayMs { get; set; } = 32000; // Max delay between retries
        public double RetryBackoffMultiplier { get; set; } = 2.0; // Exponential backoff multiplier
        public decimal CostPerRequest { get; set; } = 0.01M; // Default cost per request in USD
        public decimal DailyCostLimit { get; set; } = 50.0M; // Daily cost limit in USD
        public Dictionary<string, ProviderSettings> ProviderSpecificSettings { get; set; } = new();
    }

    public class ProviderSettings
    {
        public int? RequestsPerWindow { get; set; }
        public int? WindowSeconds { get; set; }
        public int? MaxConcurrentRequests { get; set; }
        public decimal? CostPerRequest { get; set; }
        public decimal? DailyCostLimit { get; set; }
    }
}