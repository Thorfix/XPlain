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
        var rootCommand = new RootCommand("XPlain - AI-powered code explanation tool");

        // Help Command Group
        var helpCommand = new Command("help", "Show help information");
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
        rootCommand.SetHandler(async (bool version, bool examples, bool configHelp, DirectoryInfo path,
            int verbosity, string? question, OutputFormat format, FileInfo? config, string? model) =>
        {
            if (version)
            {
                ShowVersionInfo();
                return;
            }
            if (examples)
            {
                ShowExamples();
                return;
            }
            if (configHelp)
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

    private static void ShowVersionInfo()
    {
        Console.WriteLine($"XPlain version {Version}");
        Console.WriteLine("AI-powered code explanation tool");
        Console.WriteLine("Copyright (c) 2024");
        Console.WriteLine("https://github.com/yourusername/xplain");
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
        Console.WriteLine("XPlain Configuration Guide:");
        Console.WriteLine("\nConfiguration File (appsettings.json):");
        Console.WriteLine("{\n  \"Anthropic\": {");
        Console.WriteLine("    \"ApiToken\": \"your-api-token-here\",");
        Console.WriteLine("    \"ApiEndpoint\": \"https://api.anthropic.com/v1\",");
        Console.WriteLine("    \"MaxTokenLimit\": 2000,");
        Console.WriteLine("    \"DefaultModel\": \"claude-2\"");
        Console.WriteLine("  }\n}");
        Console.WriteLine("\nEnvironment Variables:");
        Console.WriteLine("  XPLAIN_ANTHROPIC__APITOKEN=your-token-here");
        Console.WriteLine("  XPLAIN_ANTHROPIC__APIENDPOINT=https://custom-endpoint");
        Console.WriteLine("  XPLAIN_ANTHROPIC__MAXTOKENLIMIT=4000");
        Console.WriteLine("  XPLAIN_ANTHROPIC__DEFAULTMODEL=claude-2");
        Console.WriteLine("\nConfiguration Priority:");
        Console.WriteLine("1. Command-line arguments");
        Console.WriteLine("2. Environment variables");
        Console.WriteLine("3. Custom config file (if specified)");
        Console.WriteLine("4. Default appsettings.json");
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