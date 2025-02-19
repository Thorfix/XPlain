namespace XPlain.Configuration;

public class CommandLineOptions
{
    public required string CodebasePath { get; set; }
    public int VerbosityLevel { get; set; } = 1;
    public string? DirectQuestion { get; set; }
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;
    public string? ConfigPath { get; set; }
    public string? ModelName { get; set; }
    public bool InteractiveMode { get; set; } = true;
}

public enum OutputFormat
{
    Text,
    Json,
    Markdown
}