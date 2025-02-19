using System.ComponentModel;

using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

/// <summary>
/// Represents the command-line options for XPlain
/// </summary>
public class CommandLineOptions
{
    // Required Options Group
    [Required(ErrorMessage = "Codebase path is required. Please specify the directory containing your code.")]
    [Description("Path to the codebase directory to analyze")]
    public required string CodebasePath { get; set; }

    // Execution Mode Group
    [Description("Direct question to ask about the code (skips interactive mode)")]
    public string? DirectQuestion { get; set; }

    [Description("Whether to run in interactive mode (false when direct question is provided)")]
    public bool InteractiveMode { get; set; } = true;

    // Output Configuration Group
    [Description("Verbosity level (0=quiet, 1=normal, 2=verbose)")]
    [Range(0, 2, ErrorMessage = "Verbosity level must be between 0 and 2")]
    public int VerbosityLevel { get; set; } = 1;

    [Description("Output format for responses (Text, Json, or Markdown)")]
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    // Model Configuration Group
    [Description("Path to custom configuration file")]
    public string? ConfigPath { get; set; }

    [Description("Override the AI model to use")]
    [RegularExpression(@"^claude-[a-zA-Z0-9\-\.]+$", ErrorMessage = "Invalid model name format. Must start with 'claude-' followed by version.")]
    public string? ModelName { get; set; }

    public string Version => "1.0.0";

    public void Validate()
    {
        var context = new ValidationContext(this);
        var results = new List<ValidationResult>();
        
        if (!Validator.TryValidateObject(this, context, results, true))
        {
            throw new ValidationException(
                "Invalid command line options:\n" + 
                string.Join("\n", results.Select(r => $"- {r.ErrorMessage}")));
        }

        if (!string.IsNullOrEmpty(ConfigPath) && !File.Exists(ConfigPath))
        {
            throw new ValidationException($"Configuration file not found: {ConfigPath}");
        }

        if (!Directory.Exists(CodebasePath))
        {
            throw new ValidationException($"Codebase directory not found: {CodebasePath}");
        }
    }
}

/// <summary>
/// Available output formats for XPlain responses
/// </summary>
public enum OutputFormat
{
    [Description("Plain text output")]
    Text,
    
    [Description("JSON formatted output")]
    Json,
    
    [Description("Markdown formatted output")]
    Markdown
}