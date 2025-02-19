using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using XPlain.Configuration;
using XPlain.Services;

namespace XPlain;

public class Program
{
    private const string Version = "1.0.0";
    private static bool _keepRunning = true;

    public static async Task Main(string[] args)
    {
        try
        {
            var serviceProvider = ConfigureServices();
            var anthropicClient = serviceProvider.GetRequiredService<IAnthropicClient>();

            if (!await anthropicClient.ValidateApiConnection())
            {
                throw new Exception("Failed to validate Anthropic API connection. Please check your API token and connection.");
            }

            Console.WriteLine("Configuration loaded and API connection validated successfully!");

            if (args.Length > 0)
            {
                if (args[0] == "--help" || args[0] == "-h")
                {
                    ShowHelp();
                    return;
                }
                if (args[0] == "--version" || args[0] == "-v")
                {
                    Console.WriteLine($"XPlain version {Version}");
                    return;
                }
            }

            string? codeDirectory = null;
            if (args.Length > 0 && Directory.Exists(args[0]))
            {
                codeDirectory = args[0];
            }

            while (codeDirectory == null)
            {
                Console.Write("Enter the path to your code directory: ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Directory path cannot be empty.");
                    continue;
                }
                if (!Directory.Exists(input))
                {
                    Console.WriteLine("Directory does not exist. Please enter a valid path.");
                    continue;
                }
                codeDirectory = input;
            }

            Console.WriteLine($"Analyzing code directory: {codeDirectory}");
            Console.WriteLine("Enter your questions about the code. Type 'exit' to quit, 'help' for commands.");

            await StartInteractionLoop(codeDirectory);
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

    private static async Task StartInteractionLoop(string codeDirectory)
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
                    Console.WriteLine($"Processing question about code in {codeDirectory}...");
                    try
                    {
                        string codeContext = BuildCodeContext(codeDirectory);
                        string response = await anthropicClient.AskQuestion(input, codeContext);
                        Console.WriteLine("\nResponse:");
                        Console.WriteLine(response);
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

    private static void ShowHelp()
    {
        Console.WriteLine("XPlain - Code Analysis Tool");
        Console.WriteLine("Usage: XPlain [directory] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  directory     Path to the code directory to analyze");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help     Show this help message");
        Console.WriteLine("  -v, --version  Show version information");
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

    private static IServiceProvider ConfigureServices()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
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