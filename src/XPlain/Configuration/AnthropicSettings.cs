using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

public class AnthropicSettings : LLMSettings
{
    [Required(ErrorMessage = "Anthropic API token is required")]
    public string ApiToken
    {
        get => ApiKey;
        set => ApiKey = value;
    }

    [Required(ErrorMessage = "API endpoint URL is required")]
    [Url(ErrorMessage = "Invalid API endpoint URL format")]
    public string ApiEndpoint { get; set; } = "https://api.anthropic.com";

    [Range(1, 100000, ErrorMessage = "Maximum token limit must be between 1 and 100000")]
    public int MaxTokenLimit { get; set; } = 2000;

    [Required(ErrorMessage = "Default model version is required")]
    public string DefaultModel
    {
        get => Model;
        set => Model = value;
    }

    [Range(1, 10, ErrorMessage = "Maximum retry attempts must be between 1 and 10")]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(100, 10000, ErrorMessage = "Initial retry delay must be between 100 and 10000 milliseconds")]
    public int InitialRetryDelayMs { get; set; } = 1000;

    [Range(1.1, 3.0, ErrorMessage = "Backoff multiplier must be between 1.1 and 3.0")]
    public double BackoffMultiplier { get; set; } = 2.0;

    [Range(0.0, 0.3, ErrorMessage = "Jitter factor must be between 0.0 and 0.3")]
    public double JitterFactor { get; set; } = 0.1;

    [Range(0.5, 1.0, ErrorMessage = "Circuit breaker failure threshold must be between 0.5 and 1.0")]
    public double CircuitBreakerFailureThreshold { get; set; } = 0.7;

    [Range(5000, 300000, ErrorMessage = "Circuit breaker reset timeout must be between 5000 and 300000 milliseconds")]
    public int CircuitBreakerResetTimeoutMs { get; set; } = 30000;
}