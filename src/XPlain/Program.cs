using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using XPlain.Configuration;

namespace XPlain;

public class Program
{
    private const string Version = "1.0.0";
    private static bool _keepRunning = true;

    public static void Main(string[] args)
    {
        try
        {
            var services = ConfigureServices();
            var anthropicSettings = services.GetRequiredService<IOptions<AnthropicSettings>>().Value;
            Console.WriteLine("Configuration loaded successfully!");

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

            StartInteractionLoop(codeDirectory);
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

    private static void StartInteractionLoop(string codeDirectory)
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
                    // TODO: Process the question through LLM integration
                    Console.WriteLine($"Processing question about code in {codeDirectory}...");
                    Console.WriteLine("LLM integration pending implementation.");
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


    private static IServiceProvider ConfigureServices()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables("XPlain_")
            .Build();

        IServiceCollection services = new ServiceCollection();

        services.AddOptions<AnthropicSettings>()
            .Bind(configuration.GetSection("Anthropic"))
            .ValidateDataAnnotations();

        return services.BuildServiceProvider();
    }
}