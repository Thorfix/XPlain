using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.CommandLine;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using XPlain.Configuration;
using XPlain.Services;

namespace XPlain;

public class Program
{
    private const string Version = "1.0.0";
    private static bool _keepRunning = true;

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand(
            """
            XPlain - AI-powered code explanation tool

            Analyzes code and answers questions about your codebase using AI.
            Run with --help for detailed usage information.
            """);

        // Create option groups
        var requiredGroup = new Command("required", "Required options");
        var executionGroup = new Command("execution", "Execution mode options");
        var outputGroup = new Command("output", "Output configuration options");
        var modelGroup = new Command("model", "Model configuration options");
        var helpGroup = new Command("help", "Help and information options");

        // Add options to their respective groups based on OptionGroupAttribute
        var options = typeof(CommandLineOptions).GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(OptionGroupAttribute), false).Any())
            .ToList();

        foreach (var option in options)
        {
            var groupAttr = option.GetCustomAttribute<OptionGroupAttribute>();
            var descAttr = option.GetCustomAttribute<DescriptionAttribute>();
            var name = option.Name.ToLower();
            var desc = descAttr?.Description ?? name;

            Option opt = option.PropertyType switch
            {
                Type t when t == typeof(int) => new Option<int>($"--{name}", desc),
                Type t when t == typeof(bool) => new Option<bool>($"--{name}", desc),
                Type t when t == typeof(string) => new Option<string>($"--{name}", desc),
                Type t when t.IsEnum => new Option<string>($"--{name}", desc),
                _ => throw new NotSupportedException($"Unsupported option type: {option.PropertyType}")
            };

            switch (groupAttr?.Group)
            {
                case OptionGroup.Required:
                    requiredGroup.AddOption(opt);
                    break;
                case OptionGroup.ExecutionMode:
                    executionGroup.AddOption(opt);
                    break;
                case OptionGroup.Output:
                    outputGroup.AddOption(opt);
                    break;
                case OptionGroup.Model:
                    modelGroup.AddOption(opt);
                    break;
            }
        }

        // Add help options
        var helpOption = new Option<bool>(new[] { "--help", "-h" }, "Show this help message");
        var versionOption = new Option<bool>(new[] { "--version", "-v" }, "Display version information");
        var examplesOption = new Option<bool>("--examples", "Show usage examples");
        var configHelpOption = new Option<bool>("--config-help", "Display configuration options");

        helpGroup.AddOption(helpOption);
        helpGroup.AddOption(versionOption);
        helpGroup.AddOption(examplesOption);
        helpGroup.AddOption(configHelpOption);

        // Add all groups to root command
        rootCommand.AddCommand(helpGroup);
        rootCommand.AddCommand(requiredGroup);
        rootCommand.AddCommand(executionGroup);
        rootCommand.AddCommand(outputGroup);
        rootCommand.AddCommand(modelGroup);

        // Root command handler
        rootCommand.SetHandler(async (ParseResult parseResult) =>
        {
            try
            {
                // Check help options first
                if (parseResult.GetValueForOption(helpOption))
                {
                    ShowHelp();
                    return;
                }
                if (parseResult.GetValueForOption(versionOption))
                {
                    ShowVersionInfo();
                    return;
                }
                if (parseResult.GetValueForOption(examplesOption))
                {
                    ShowExamples();
                    return;
                }
                if (parseResult.GetValueForOption(configHelpOption))
                {
                    ShowConfigHelp();
                    return;
                }

                // Create options instance from parsed values
                var options = new CommandLineOptions
                {
                    CodebasePath = parseResult.GetValueForOption(requiredGroup.Options.First(o => o.Name == "codebasepath")) ?? "",
                    VerbosityLevel = parseResult.GetValueForOption(outputGroup.Options.First(o => o.Name == "verbosity")),
                    DirectQuestion = parseResult.GetValueForOption(executionGroup.Options.First(o => o.Name == "question")),
                    OutputFormat = Enum.Parse<OutputFormat>(parseResult.GetValueForOption(outputGroup.Options.First(o => o.Name == "format")) ?? "Text"),
                    ConfigPath = parseResult.GetValueForOption(modelGroup.Options.First(o => o.Name == "config")),
                    ModelName = parseResult.GetValueForOption(modelGroup.Options.First(o => o.Name == "model")),
                    InteractiveMode = string.IsNullOrEmpty(parseResult.GetValueForOption(executionGroup.Options.First(o => o.Name == "question")))
                };

                // Validate options
                options.Validate();

                // Process the command with validated options
                await ProcessCommand(options);
            }
            catch (ValidationException ex)
            {
                Console.Error.WriteLine("Validation error:");
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });
        
        // Override default help with our custom help
        rootCommand.HelpOption.SetHandler(() => {
            ShowHelp();
            Environment.Exit(0);
        });

        // Help Options Group
        var helpOption = new Option<bool>(
            aliases: new[] { "--help", "-h" },
            description: "Show this help message");
        
        var versionOption = new Option<bool>(
            aliases: new[] { "--version", "-v" },
            description: "Display version information");
        
        var examplesOption = new Option<bool>(
            aliases: new[] { "--examples" },
            description: "Show usage examples");
        
        var configHelpOption = new Option<bool>(
            aliases: new[] { "--config-help" },
            description: "Display configuration options and environment variables");

        // Required Arguments
        var pathArgument = new Argument<DirectoryInfo>(
            name: "codebase-path",
            description: "Path to the codebase directory to analyze");
        pathArgument.AddValidator(result =>
        {
            if (result.GetValueOrDefault<DirectoryInfo>()?.Exists != true)
            {
                result.ErrorMessage = "The specified codebase directory does not exist.";
            }
        });

        // Execution Mode Options Group
        var executionModeGroup = new Command("mode", "Execution mode options");
        var questionOption = new Option<string?>(
            aliases: new[] { "--question", "-q" },
            description: "Direct question to ask about the code (skips interactive mode)");

        // Output Configuration Group
        var outputGroup = new Command("output", "Output configuration options");
        var verbosityOption = new Option<int>(
            aliases: new[] { "--verbosity" },
            getDefaultValue: () => 1,
            description: "Verbosity level (0=quiet, 1=normal, 2=verbose)");
        verbosityOption.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<int>();
            if (value < 0 || value > 2)
            {
                result.ErrorMessage = "Verbosity level must be between 0 and 2.";
            }
        });

        var outputFormatOption = new Option<OutputFormat>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => OutputFormat.Text,
            description: "Output format (text, json, or markdown)");

        // Model Configuration Group
        var modelGroup = new Command("model", "Model configuration options");
        var configOption = new Option<FileInfo?>(
            aliases: new[] { "--config", "-c" },
            description: "Path to custom configuration file");
        configOption.AddValidator(result =>
        {
            var file = result.GetValueOrDefault<FileInfo>();
            if (file != null && !file.Exists)
            {
                result.ErrorMessage = "The specified configuration file does not exist.";
            }
        });

        var modelOption = new Option<string?>(
            aliases: new[] { "--model", "-m" },
            description: "Override the AI model to use");
        modelOption.AddValidator(result =>
        {
            var model = result.GetValueOrDefault<string>();
            if (model != null && !model.StartsWith("claude-"))
            {
                result.ErrorMessage = "Model name must start with 'claude-'.";
            }
        });

        // Add options to their respective groups
        executionModeGroup.AddOption(questionOption);
        
        outputGroup.AddOption(verbosityOption);
        outputGroup.AddOption(outputFormatOption);
        
        modelGroup.AddOption(configOption);
        modelGroup.AddOption(modelOption);

        // Add all components to root command
        rootCommand.AddOption(versionOption);
        rootCommand.AddOption(examplesOption);
        rootCommand.AddOption(configHelpOption);
        
        rootCommand.AddArgument(pathArgument);
        rootCommand.AddCommand(executionModeGroup);
        rootCommand.AddCommand(outputGroup);
        rootCommand.AddCommand(modelGroup);

        // Special handlers for help commands
        rootCommand.SetHandler(async (bool help, bool version, bool examples, bool configHelp, DirectoryInfo path,
            int verbosity, string? question, OutputFormat format, FileInfo? config, string? model) =>
        {
            if (help)
            {
                ShowHelp();
                return;
            }
            else if (version)
            {
                ShowVersionInfo();
                return;
            }
            else if (examples)
            {
                ShowExamples();
                return;
            }
            else if (configHelp)
            {
                ShowConfigHelp();
                return;
            }

        rootCommand.SetHandler(async (DirectoryInfo path, int verbosity, string? question,
            OutputFormat format, FileInfo? config, string? model) =>
        {
            try
            {
                var options = new CommandLineOptions
                {
                    CodebasePath = path.FullName,
                    VerbosityLevel = verbosity,
                    DirectQuestion = question,
                    OutputFormat = format,
                    ConfigPath = config?.FullName,
                    ModelName = model,
                    InteractiveMode = string.IsNullOrEmpty(question)
                };

                // Validate all options before proceeding
                options.Validate();

                if (verbosity >= 1)
                {
                    Console.WriteLine($"Analyzing code directory: {options.CodebasePath}");
                }

                var serviceProvider = ConfigureServices(options);
                var anthropicClient = serviceProvider.GetRequiredService<IAnthropicClient>();

                if (!await anthropicClient.ValidateApiConnection())
                {
                    throw new Exception("Failed to validate Anthropic API connection. Please check your API token and connection.");
                }

                if (verbosity >= 1)
                {
                    Console.WriteLine("Configuration loaded and API connection validated successfully!");
                }

                if (options.InteractiveMode)
                {
                    if (verbosity >= 1)
                    {
                        Console.WriteLine("Enter your questions about the code. Type 'exit' to quit, 'help' for commands.");
                    }
                    await StartInteractionLoop(anthropicClient, options);
                }
                else
                {
                    string codeContext = BuildCodeContext(options.CodebasePath);
                    string response = await anthropicClient.AskQuestion(options.DirectQuestion!, codeContext);
                    OutputResponse(response, options.OutputFormat);
                }
        }
        catch (OptionsValidationException ex)
        {
            Console.Error.WriteLine("Configuration validation failed:");
            foreach (var failure in ex.Failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error initializing application: {ex.Message}");
            Environment.Exit(1);
        }
    }

        });

        return await rootCommand.InvokeAsync(args);
    }

    private static void OutputResponse(string response, OutputFormat format)
    {
        switch (format)
        {
            case OutputFormat.Json:
                var jsonObject = new { response = response };
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                Console.WriteLine(JsonSerializer.Serialize(jsonObject, jsonOptions));
                break;
            case OutputFormat.Markdown:
                Console.WriteLine("```markdown");
                Console.WriteLine(response);
                Console.WriteLine("```");
                break;
            case OutputFormat.Text:
            default:
                Console.WriteLine(response);
                break;
        }
    }

    private static async Task StartInteractionLoop(IAnthropicClient anthropicClient, CommandLineOptions options)
    {
        while (_keepRunning)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            switch (input.ToLower())
            {
                case "exit":
                case "quit":
                    _keepRunning = false;
                    break;

                case "help":
                    ShowInteractiveHelp();
                    break;

                case "version":
                    Console.WriteLine($"XPlain version {Version}");
                    break;

                default:
                    if (options.VerbosityLevel >= 1)
                    {
                        Console.WriteLine($"Processing question about code in {options.CodebasePath}...");
                    }
                    try
                    {
                        string codeContext = BuildCodeContext(options.CodebasePath);
                        string response = await anthropicClient.AskQuestion(input, codeContext);
                        if (options.VerbosityLevel >= 1)
                        {
                            Console.WriteLine("\nResponse:");
                        }
                        OutputResponse(response, options.OutputFormat);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing question: {ex.Message}");
                    }
                    break;
            }
        }

        Console.WriteLine("Goodbye!");
    }

    private static void ShowInteractiveHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("Help and Information:");
        Console.WriteLine("  help     - Show this help message");
        Console.WriteLine("  version  - Show version information");
        Console.WriteLine();
        Console.WriteLine("Navigation:");
        Console.WriteLine("  exit     - Exit the application");
        Console.WriteLine("  quit     - Exit the application");
        Console.WriteLine();
        Console.WriteLine("Type your questions about the code to analyze them.");
    }

    private static void ShowHelp()
    {
        var optionGroups = typeof(OptionGroup).GetEnumValues()
            .Cast<OptionGroup>()
            .ToDictionary(
                g => g,
                g => typeof(CommandLineOptions).GetProperties()
                    .Where(p => p.GetCustomAttribute<OptionGroupAttribute>()?.Group == g)
                    .Select(p => (
                        Name: p.Name.ToLower(),
                        Desc: p.GetCustomAttribute<DescriptionAttribute>()?.Description,
                        Validation: p.GetCustomAttribute<ValidationAttribute>()?.ErrorMessage
                    ))
                    .ToList()
            );

        var helpText = new System.Text.StringBuilder();
        helpText.AppendLine("XPlain - AI-powered code explanation tool");
        helpText.AppendLine();
        helpText.AppendLine("USAGE:");
        helpText.AppendLine("  xplain <codebase-path> [options]");
        helpText.AppendLine();
        
        // Help and Information
        helpText.AppendLine("HELP AND INFORMATION:");
        helpText.AppendLine("  -h, --help       Show this help message");
        helpText.AppendLine("  -v, --version    Display version information");
        helpText.AppendLine("  --examples       Show usage examples");
        helpText.AppendLine("  --config-help    Display configuration options");
        helpText.AppendLine();

        // Other option groups
        foreach (var group in optionGroups)
        {
            var groupName = group.Key.ToString().ToUpper();
            var groupDesc = typeof(OptionGroup)
                .GetField(group.Key.ToString())
                ?.GetCustomAttribute<DescriptionAttribute>()
                ?.Description;

            helpText.AppendLine($"{groupName} OPTIONS: {groupDesc}");
            
            foreach (var option in group.Value)
            {
                helpText.AppendLine($"  --{option.Name,-12} {option.Desc}");
                if (option.Validation != null)
                {
                    helpText.AppendLine($"                   Validation: {option.Validation}");
                }
            }
            helpText.AppendLine();
        }

        // Additional Information
        helpText.AppendLine("For more information:");
        helpText.AppendLine("  Use --examples to see usage examples");
        helpText.AppendLine("  Use --config-help to see configuration options");
        helpText.AppendLine("  Use --version to see version information");
        helpText.AppendLine();
        
        helpText.AppendLine("Examples:");
        helpText.AppendLine("  xplain ./my-project");
        helpText.AppendLine("  xplain ./my-project -q \"What does Program.cs do?\"");
        helpText.AppendLine("  xplain ./my-project -f markdown --verbosity 2");

        Console.WriteLine(helpText.ToString());
    }

    private static void ShowVersionInfo()
    {
        Console.WriteLine($"""
            XPlain version {Version}
            AI-powered code explanation tool
            Copyright (c) 2024
            https://github.com/yourusername/xplain
            
            Using System.CommandLine for CLI parsing
            Powered by Anthropic's Claude AI
            """);
    }

    private static void ShowExamples()
    {
        Console.WriteLine("XPlain Usage Examples:");
        Console.WriteLine("\nRequired Arguments:");
        Console.WriteLine("  xplain ./my-project");
        Console.WriteLine("  # Starts interactive mode for the specified codebase");
        
        Console.WriteLine("\nExecution Mode Options:");
        Console.WriteLine("  xplain ./my-project -q \"What does the Program.cs file do?\"");
        Console.WriteLine("  # Gets immediate answer for a specific question");
        
        Console.WriteLine("\nOutput Configuration:");
        Console.WriteLine("  xplain ./my-project -f markdown --verbosity 2");
        Console.WriteLine("  # Gets response in markdown format with detailed logging");
        Console.WriteLine("  xplain ./my-project -f json -q \"List all classes\"");
        Console.WriteLine("  # Gets response in JSON format");
        
        Console.WriteLine("\nModel Configuration:");
        Console.WriteLine("  xplain ./my-project -c custom-settings.json -m claude-3-opus-20240229");
        Console.WriteLine("  # Uses custom configuration and specific model version");
        
        Console.WriteLine("\nCombined Examples:");
        Console.WriteLine("  xplain ./my-project -f markdown --verbosity 2 -m claude-3-opus-20240229 \\");
        Console.WriteLine("    -q \"Analyze the architecture and provide a detailed breakdown\"");
        Console.WriteLine("  # Combines multiple options for detailed analysis");
    }

    private static void ShowConfigHelp()
    {
        Console.WriteLine("""
            XPlain Configuration Guide
            ========================

            Configuration Methods (in order of priority):
            1. Command-line arguments (highest priority)
            2. Environment variables
            3. Custom config file (if specified)
            4. Default appsettings.json (lowest priority)

            Configuration File (appsettings.json):
            ------------------------------------
            {
              "Anthropic": {
                "ApiToken": "your-api-token-here",      # Required: API token for Claude
                "ApiEndpoint": "https://api.anthropic.com/v1",
                "MaxTokenLimit": 2000,                  # Maximum tokens per request
                "DefaultModel": "claude-2"              # Default AI model to use
              }
            }

            Environment Variables:
            -------------------
            XPLAIN_ANTHROPIC__APITOKEN=your-token-here
            XPLAIN_ANTHROPIC__APIENDPOINT=https://api.anthropic.com/v1
            XPLAIN_ANTHROPIC__MAXTOKENLIMIT=4000
            XPLAIN_ANTHROPIC__DEFAULTMODEL=claude-2

            Configuration Options:
            -------------------
            ApiToken:      (Required) Your Anthropic API token
            ApiEndpoint:   (Optional) Custom API endpoint
            MaxTokenLimit: (Optional) Maximum tokens per request (default: 2000)
            DefaultModel:  (Optional) Default AI model (default: claude-2)

            Notes:
            - Environment variables override appsettings.json
            - Command-line options override both
            - Use -c/--config to specify a custom config file
            - API token is required and must be set in config or environment
            """);
    }


    private static string BuildCodeContext(string codeDirectory)
    {
        var context = new System.Text.StringBuilder();
        context.AppendLine("Code files in the directory:");

        var files = Directory.GetFiles(codeDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".cs" or ".fs" or ".vb" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h");

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(codeDirectory, file);
            context.AppendLine($"\nFile: {relativePath}");
            context.AppendLine(File.ReadAllText(file));
        }

        return context.ToString();
    }

    private static IServiceProvider ConfigureServices(CommandLineOptions options)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(options.ConfigPath ?? "appsettings.override.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables("XPLAIN_")
            .Build();
        
        IServiceCollection services = new ServiceCollection();

        services.AddOptions<AnthropicSettings>()
            .Bind(configuration.GetSection("Anthropic"))
            .ValidateDataAnnotations();

        services.AddHttpClient();
        services.AddSingleton<IAnthropicClient, AnthropicClient>();

        return services.BuildServiceProvider();
    }
}