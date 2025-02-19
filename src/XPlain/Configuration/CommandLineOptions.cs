using System.ComponentModel;

namespace XPlain.Configuration;

/// <summary>
/// Represents the command-line options for XPlain
/// </summary>
public class CommandLineOptions
{
    [Description("Path to the codebase directory to analyze")]
    public required string CodebasePath { get; set; }

    [Description("Verbosity level (0=quiet, 1=normal, 2=verbose)")]
    public int VerbosityLevel { get; set; } = 1;

    [Description("Direct question to ask about the code (skips interactive mode)")]
    public string? DirectQuestion { get; set; }

    [Description("Output format for responses (Text, Json, or Markdown)")]
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    [Description("Path to custom configuration file")]
    public string? ConfigPath { get; set; }

    [Description("Override the AI model to use")]
    public string? ModelName { get; set; }

    [Description("Whether to run in interactive mode (false when direct question is provided)")]
    public bool InteractiveMode { get; set; } = true;

    public string Version => "1.0.0";
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