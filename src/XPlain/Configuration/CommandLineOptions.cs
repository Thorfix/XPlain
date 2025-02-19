using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

/// <summary>
/// Attribute to specify which option group a property belongs to
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class OptionGroupAttribute : Attribute
{
    public OptionGroup Group { get; }

    public OptionGroupAttribute(OptionGroup group)
    {
        Group = group;
    }
}

/// <summary>
/// Groups for command-line options
/// </summary>
public enum OptionGroup
{
    [Description("Required options that must be specified")]
    Required,

    [Description("Options controlling how the tool executes")]
    ExecutionMode,

    [Description("Options controlling output and verbosity")]
    Output,

    [Description("Options for configuring the AI model")]
    Model
}

/// <summary>
/// Represents the command-line options for XPlain
/// </summary>
public class CommandLineOptions
{
    #region Required Options Group
    [Required(ErrorMessage = "Codebase path is required. Please specify the directory containing your code using: xplain <path-to-code>")]
    [Description("Path to the codebase directory to analyze")]
    [OptionGroup(OptionGroup.Required)]
    public required string CodebasePath { get; set; }
    #endregion

    #region Execution Mode Options
    [Description("Direct question to ask about the code (skips interactive mode)")]
    [RegularExpression(@".{10,}", ErrorMessage = "Question must be at least 10 characters long to be meaningful. Example: --question \"What does the Program.cs file do?\"")]
    [OptionGroup(OptionGroup.ExecutionMode)]
    public string? DirectQuestion { get; set; }

    [Description("Whether to run in interactive mode (false when direct question is provided)")]
    [OptionGroup(OptionGroup.ExecutionMode)]
    public bool InteractiveMode { get; set; } = true;
    #endregion

    #region Output Options
    [Description("Verbosity level (0=quiet, 1=normal, 2=verbose)")]
    [Range(0, 2, ErrorMessage = "Verbosity level must be between 0 and 2. Example: --verbosity 2 for detailed output")]
    [OptionGroup(OptionGroup.Output)]
    public int VerbosityLevel { get; set; } = 1;

    [Description("Output format for responses (Text, Json, or Markdown)")]
    [EnumDataType(typeof(OutputFormat), ErrorMessage = "Output format must be one of: Text, Json, Markdown. Example: --format markdown")]
    [OptionGroup(OptionGroup.Output)]
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;
    #endregion

    #region Model Configuration Options
    [Description("Path to custom configuration file")]
    [FileExtensions(Extensions = "json", ErrorMessage = "Configuration file must be a .json file. Example: --config custom-settings.json")]
    [OptionGroup(OptionGroup.Model)]
    public string? ConfigPath { get; set; }

    [Description("Override the AI model to use")]
    [RegularExpression(@"^claude-[a-zA-Z0-9\-\.]+$", 
        ErrorMessage = "Invalid model name format. Must start with 'claude-' followed by version. Example: --model claude-3-opus-20240229")]
    [OptionGroup(OptionGroup.Model)]
    public string? ModelName { get; set; }
    #endregion

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