using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class OptionGroupAttribute : Attribute
{
    public OptionGroup Group { get; }

    public OptionGroupAttribute(OptionGroup group)
    {
        Group = group;
    }
}

public enum OptionGroup
{
    [Description("Required options for the application")]
    Required,

    [Description("Options for controlling execution mode")]
    ExecutionMode,

    [Description("Options for controlling output")]
    Output,

    [Description("Options for configuring the LLM model")]
    Model
}

public enum OutputFormat
{
    Text,
    Json,
    Markdown
}

public class CommandLineOptions
{
    [OptionGroup(OptionGroup.Required)]
    [Description("Path to the code directory to analyze")]
    [Required(ErrorMessage = "Codebase path is required")]
    public string CodebasePath { get; set; } = string.Empty;

    [OptionGroup(OptionGroup.ExecutionMode)]
    [Description("Direct question about the code (if not provided, enters interactive mode)")]
    public string? DirectQuestion { get; set; }

    [OptionGroup(OptionGroup.Output)]
    [Description("Output verbosity level (0=quiet, 1=normal, 2=verbose)")]
    [Range(0, 2, ErrorMessage = "Verbosity level must be between 0 and 2")]
    public int VerbosityLevel { get; set; } = 1;

    [OptionGroup(OptionGroup.Output)]
    [Description("Output format (text, json, markdown)")]
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    [OptionGroup(OptionGroup.Model)]
    [Description("Path to custom configuration file")]
    public string? ConfigPath { get; set; }

    [OptionGroup(OptionGroup.Model)]
    [Description("LLM provider to use (e.g., Anthropic)")]
    public string Provider { get; set; } = "Anthropic";

    [OptionGroup(OptionGroup.Model)]
    [Description("Model name to use with the selected provider")]
    public string? ModelName { get; set; }

    public bool InteractiveMode => string.IsNullOrEmpty(DirectQuestion);

    public void Validate()
    {
        var validationContext = new ValidationContext(this);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(this, validationContext, validationResults, true))
        {
            throw new OptionsValidationException("Invalid command line options", validationResults);
        }

        if (!Directory.Exists(CodebasePath))
        {
            throw new OptionsValidationException("Invalid codebase path", 
                new[] { new ValidationResult("The specified codebase path does not exist", new[] { nameof(CodebasePath) }) });
        }

        if (!string.IsNullOrEmpty(ConfigPath) && !File.Exists(ConfigPath))
        {
            throw new OptionsValidationException("Invalid config path",
                new[] { new ValidationResult("The specified configuration file does not exist", new[] { nameof(ConfigPath) }) });
        }
    }
}

public class OptionsValidationException : Exception
{
    public IEnumerable<ValidationResult> ValidationResults { get; }

    public OptionsValidationException(string message, IEnumerable<ValidationResult> validationResults)
        : base(message)
    {
        ValidationResults = validationResults;
    }
}