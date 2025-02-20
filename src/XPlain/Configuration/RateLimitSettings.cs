namespace XPlain.Configuration
{
    public class RateLimitSettings
    {
        public int RequestsPerWindow { get; set; } = 50;
        public int WindowSeconds { get; set; } = 60;
        public int MaxConcurrentRequests { get; set; } = 5;
        public int DefaultRetryCount { get; set; } = 3;
        public int InitialRetryDelayMs { get; set; } = 1000;
    }
}