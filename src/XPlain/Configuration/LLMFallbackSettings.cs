using System.Collections.Generic;

namespace XPlain.Configuration
{
    public class LLMFallbackSettings
    {
        public bool Enabled { get; set; }
        public int RetryAttempts { get; set; }
        public List<LLMProviderConfig> Providers { get; set; } = new();
    }

    public class LLMProviderConfig
    {
        public string Name { get; set; }
        public int Priority { get; set; }
        public int? RetryAttempts { get; set; }
    }
}