using System.Collections.Generic;

namespace XPlain.Configuration
{
    public class LLMFallbackSettings
    {
        /// <summary>
        /// Whether fallback is enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Maximum number of fallback attempts before giving up
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// List of fallback providers in order of preference
        /// </summary>
        public List<ProviderConfig> Providers { get; set; } = new List<ProviderConfig>();

        /// <summary>
        /// Whether to use the fallback provider with the best latency
        /// </summary>
        public bool SelectByLatency { get; set; } = false;

        /// <summary>
        /// Whether to use the fallback provider with the best availability
        /// </summary>
        public bool SelectByAvailability { get; set; } = true;

        /// <summary>
        /// Configuration for a fallback provider
        /// </summary>
        public class ProviderConfig
        {
            /// <summary>
            /// Name of the provider
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Priority of the provider (lower is higher priority)
            /// </summary>
            public int Priority { get; set; }
        }
    }
}