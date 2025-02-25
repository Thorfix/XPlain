using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration;

public enum OutputFormat
{
    Text,
    Json,
    Markdown
}

public enum OptionGroup
{
    [Description("Required options for the command")]
    Required,
    
    [Description("Options controlling how the command executes")]
    ExecutionMode,
    
    [Description("Options controlling the output format and verbosity")]
    Output,
    
    [Description("Options controlling the AI model and configuration")]
    Model
}

[AttributeUsage(AttributeTargets.Property)]
public class OptionGroupAttribute : Attribute
{
    public OptionGroup Group { get; }
    
    public OptionGroupAttribute(OptionGroup group)
    {
        Group = group;
    }
}

public class CommandLineOptions
{
    [OptionGroup(OptionGroup.Required)]
    [Description("Path to the codebase to analyze")]
    [Required(ErrorMessage = "Codebase path is required")]
    public string CodebasePath { get; set; }
    
    [OptionGroup(OptionGroup.ExecutionMode)]
    [Description("Direct question to ask about the code (enables non-interactive mode)")]
    public string DirectQuestion { get; set; }
    
    [OptionGroup(OptionGroup.Output)]
    [Description("Format for output (text, json, markdown)")]
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;
    
    [OptionGroup(OptionGroup.Output)]
    [Description("Verbosity level (0=quiet, 1=normal, 2=verbose)")]
    [Range(0, 2, ErrorMessage = "Verbosity level must be between 0 and 2")]
    public int VerbosityLevel { get; set; } = 1;
    
    [OptionGroup(OptionGroup.Model)]
    [Description("Path to config file with API keys and model settings")]
    public string ConfigPath { get; set; }
    
    [OptionGroup(OptionGroup.Model)]
    [Description("Provider to use (anthropic, openai, azureopenai)")]
    public string Provider { get; set; }
    
    [OptionGroup(OptionGroup.Model)]
    [Description("Model name to use")]
    public string ModelName { get; set; }
    
    [OptionGroup(OptionGroup.Model)]
    [Description("API key to use (overrides config file)")]
    public string ApiKey { get; set; }
    
    [OptionGroup(OptionGroup.ExecutionMode)]
    [Description("Enable streaming responses in real-time")]
    public static bool EnableStreaming { get; set; } = false;
    
    [OptionGroup(OptionGroup.Output)]
    [Description("Show cache statistics")]
    public bool ShowCacheStats { get; set; } = false;
    
    public bool InteractiveMode { get; set; } = true;
    
    public void Validate()
    {
        if (string.IsNullOrEmpty(CodebasePath))
            throw new ValidationException("Codebase path is required");
            
        if (!System.IO.Directory.Exists(CodebasePath))
            throw new ValidationException($"Directory not found: {CodebasePath}");
    }
}