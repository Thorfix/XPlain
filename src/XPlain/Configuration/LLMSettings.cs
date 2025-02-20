namespace XPlain.Configuration
{
    public class LLMSettings
    {
        public string Provider { get; set; }
        public string Model { get; set; }
        public string ApiKey { get; set; }
        public int TimeoutSeconds { get; set; } = 30;
        public LLMFallbackSettings Fallback { get; set; }
    }
}