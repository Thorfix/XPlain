using System;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

/// <summary>
/// Settings specific to the Anthropic Claude provider
/// </summary>
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
    
    public override void Validate()
    {
        // Call base validation with empty check bypass to avoid double validation
        if (string.IsNullOrWhiteSpace(Provider))
            throw new ValidationException("Provider is required");
            
        if (string.IsNullOrWhiteSpace(Model))
            throw new ValidationException("Model is required");
            
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new ValidationException("API key is required");
            
        if (TimeoutSeconds < 5 || TimeoutSeconds > 300)
            throw new ValidationException("Timeout must be between 5 and 300 seconds");
            
        if (string.IsNullOrEmpty(ApiToken))
            throw new ValidationException("Anthropic API token is required");
            
        if (string.IsNullOrEmpty(ApiEndpoint))
            throw new ValidationException("API endpoint is required");
            
        if (MaxTokenLimit <= 0)
            throw new ValidationException("Maximum token limit must be greater than zero");
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
    
    [Range(1000, 300000, ErrorMessage = "Maximum retry delay must be between 1000 and 300000 milliseconds")]
    public int MaxRetryDelayMs { get; set; } = 30000;
    
    [Range(5, 300, ErrorMessage = "Timeout must be between 5 and 300 seconds")]
    public override int TimeoutSeconds { get; set; } = 30;
}