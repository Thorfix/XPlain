namespace XPlain.Configuration
{
    public class AzureOpenAISettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string DeploymentId { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiVersion { get; set; } = "2024-02-15-preview";
        
        // Optional: Support for managed identity
        public bool UseManagedIdentity { get; set; } = false;
    }
}