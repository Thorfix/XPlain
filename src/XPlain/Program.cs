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

        // Required path argument
        var pathArgument = new Argument<DirectoryInfo>(
            name: "codebase-path",
            description: "Path to the codebase directory to analyze");

        // Options
        var verbosityOption = new Option<int>(
            aliases: new[] { "--verbosity", "-v" },
            getDefaultValue: () => 1,
            description: "Verbosity level (0=quiet, 1=normal, 2=verbose)");

        var questionOption = new Option<string?>(
            aliases: new[] { "--question", "-q" },
            description: "Direct question to ask about the code (skips interactive mode)");

        var outputFormatOption = new Option<OutputFormat>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => OutputFormat.Text,
            description: "Output format (text, json, or markdown)");

        var configOption = new Option<FileInfo?>(
            aliases: new[] { "--config", "-c" },
            description: "Path to custom configuration file");

        var modelOption = new Option<string?>(
            aliases: new[] { "--model", "-m" },
            description: "Override the AI model to use");

        // Add options to command
        rootCommand.AddArgument(pathArgument);
        rootCommand.AddOption(verbosityOption);
        rootCommand.AddOption(questionOption);
        rootCommand.AddOption(outputFormatOption);
        rootCommand.AddOption(configOption);
        rootCommand.AddOption(modelOption);

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
        Console.WriteLine("  help     - Show this help message");
        Console.WriteLine("  version  - Show version information");
        Console.WriteLine("  exit     - Exit the application");
        Console.WriteLine("  quit     - Exit the application");
        Console.WriteLine();
        Console.WriteLine("Type your questions about the code to analyze them.");
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