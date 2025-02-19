namespace XPlain.Configuration
{
    public class LLMSettings
    {
        /// <summary>
        /// The type of LLM provider to use
        /// </summary>
        public string Provider { get; set; } = "Anthropic";

        /// <summary>
        /// The model to use for the selected provider
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// The API key for the LLM service
        /// </summary>
        public string ApiKey { get; set; }
    }
}