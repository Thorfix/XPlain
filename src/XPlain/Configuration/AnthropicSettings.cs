using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

public class AnthropicSettings
{
    [Required(ErrorMessage = "Anthropic API token is required")]
    public string ApiToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "API endpoint URL is required")]
    [Url(ErrorMessage = "Invalid API endpoint URL format")]
    public string ApiEndpoint { get; set; } = "https://api.anthropic.com";

    [Range(1, 100000, ErrorMessage = "Maximum token limit must be between 1 and 100000")]
    public int MaxTokenLimit { get; set; } = 2000;

    [Required(ErrorMessage = "Default model version is required")]
    public string DefaultModel { get; set; } = "claude-2";
}