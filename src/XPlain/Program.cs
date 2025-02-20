using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using XPlain.Configuration;
using XPlain.Services;

namespace XPlain;

file class Program
    {
        private const string Version = "1.0.0";
        static bool _keepRunning = true;

        internal static async Task ProcessCommandInternal(CommandLineOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            await ExecuteWithOptions(options);
        }

        internal static async Task<int> Main(string[] args)
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
            var helpOption = new Option<bool>(new[] {"--help", "-h"}, "Show this help message");
            var versionOption = new Option<bool>(new[] {"--version", "-v"}, "Display version information");
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
                    // Handle special help options first
                    if (parseResult.GetValueForOption(helpOption) == true ||
                        parseResult.GetValueForOption(helpGroup.Options.First(o => o.Name == "help")) == true)
                    {
                        ShowHelp();
                        return;
                    }

                    if (parseResult.GetValueForOption(versionOption) == true ||
                        parseResult.GetValueForOption(helpGroup.Options.First(o => o.Name == "version")) == true)
                    {
                        ShowVersionInfo();
                        return;
                    }

                    if (parseResult.GetValueForOption(examplesOption) == true ||
                        parseResult.GetValueForOption(helpGroup.Options.First(o => o.Name == "examples")) == true)
                    {
                        ShowExamples();
                        return;
                    }

                    if (parseResult.GetValueForOption(configHelpOption) == true ||
                        parseResult.GetValueForOption(helpGroup.Options.First(o => o.Name == "config-help")) == true)
                    {
                        ShowConfigHelp();
                        return;
                    }

                    // Parse options from groups
                    var options = new CommandLineOptions();

                    // Required options
                    var requiredOpts = requiredGroup.Options.ToDictionary(o => o.Name);
                    options.CodebasePath = parseResult.GetValueForOption<string>(requiredOpts["codebasepath"]) ?? "";

                    // Execution options
                    var execOpts = executionGroup.Options.ToDictionary(o => o.Name);
                    options.DirectQuestion = parseResult.GetValueForOption<string>(execOpts["directquestion"]);
                    options.InteractiveMode = string.IsNullOrEmpty(options.DirectQuestion);

                    // Output options
                    var outputOpts = outputGroup.Options.ToDictionary(o => o.Name);
                    options.VerbosityLevel = parseResult.GetValueForOption<int>(outputOpts["verbositylevel"]);
                    var formatStr = parseResult.GetValueForOption<string>(outputOpts["outputformat"]);
                    options.OutputFormat = string.IsNullOrEmpty(formatStr)
                        ? OutputFormat.Text
                        : Enum.Parse<OutputFormat>(formatStr);

                    // Model options
                    var modelOpts = modelGroup.Options.ToDictionary(o => o.Name);
                    options.ConfigPath = parseResult.GetValueForOption<string>(modelOpts["configpath"]);
                    options.ModelName = parseResult.GetValueForOption<string>(modelOpts["modelname"]);

                    // Validate options
                    options.Validate();

                    // Process the command with validated options
                    try
                    {
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
                    finally
                    {
                        if (options?.VerbosityLevel >= 2)
                        {
                            Console.WriteLine("Command processing completed.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error in command processing: {ex.Message}");
                    Environment.Exit(1);
                }
            });

            // Override default help with our custom help
            rootCommand.HelpOption.SetHandler(() =>
            {
                ShowHelp();
                Environment.Exit(0);
            });

            return await rootCommand.InvokeAsync(args);
        }

        internal static async Task ShowCacheStats(ICacheProvider cacheProvider, OutputFormat format)
        {
            var stats = cacheProvider.GetCacheStats();
            var chart = await cacheProvider.GeneratePerformanceChartAsync(format);
            var recommendations = await cacheProvider.GetCacheWarmingRecommendationsAsync();
            
            var output = new
            {
                HitRatio = $"{stats.HitRatio:P2}",
                Hits = stats.Hits,
                Misses = stats.Misses,
                CachedItems = stats.CachedItemCount,
                StorageUsage = $"{stats.StorageUsageBytes / 1024.0 / 1024.0:F2} MB",
                TopQueries = stats.TopQueries,
                QueryTypes = stats.QueryTypeStats,
                PerformanceByQueryType = stats.PerformanceByQueryType.ToDictionary(
                    kvp => kvp.Key,
                    kvp => $"{kvp.Value.PerformanceGain:F1}% faster (cached: {kvp.Value.CachedResponseTime:F2}ms, non-cached: {kvp.Value.NonCachedResponseTime:F2}ms)"
                ),
                InvalidationStats = new
                {
                    TotalInvalidations = stats.InvalidationCount,
                    RecentInvalidations = stats.InvalidationHistory.TakeLast(5).ToList()
                },
                Recommendations = recommendations,
                LastUpdate = stats.LastStatsUpdate,
                PerformanceCharts = chart
            };

            switch (format)
            {
                case OutputFormat.Json:
                    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
                    break;
                case OutputFormat.Markdown:
                    Console.WriteLine("# Cache Statistics");
                    Console.WriteLine($"## Performance");
                    Console.WriteLine($"- Hit Ratio: {output.HitRatio}");
                    Console.WriteLine($"- Cache Hits: {output.Hits}");
                    Console.WriteLine($"- Cache Misses: {output.Misses}");
                    Console.WriteLine($"\n## Storage");
                    Console.WriteLine($"- Cached Items: {output.CachedItems}");
                    Console.WriteLine($"- Storage Usage: {output.StorageUsage}");
                    Console.WriteLine($"\n## Top Queries");
                    foreach (var (query, freq) in output.TopQueries)
                    {
                        Console.WriteLine($"- {query}: {freq} times");
                    }
                    Console.WriteLine($"\n## Query Types");
                    foreach (var (type, count) in output.QueryTypes)
                    {
                        Console.WriteLine($"- {type}: {count} queries");
                    }
                    Console.WriteLine($"\n## Average Response Times");
                    foreach (var (type, time) in output.AverageResponseTimes)
                    {
                        Console.WriteLine($"- {type}: {time}");
                    }
                    Console.WriteLine($"\nLast Updated: {output.LastUpdate:yyyy-MM-dd HH:mm:ss UTC}");
                    break;
                default:
                    Console.WriteLine("Cache Statistics");
                    Console.WriteLine("================");
                    Console.WriteLine($"Performance:");
                    Console.WriteLine($"  Hit Ratio: {output.HitRatio}");
                    Console.WriteLine($"  Cache Hits: {output.Hits}");
                    Console.WriteLine($"  Cache Misses: {output.Misses}");
                    Console.WriteLine($"\nStorage:");
                    Console.WriteLine($"  Cached Items: {output.CachedItems}");
                    Console.WriteLine($"  Storage Usage: {output.StorageUsage}");
                    Console.WriteLine($"\nTop Queries:");
                    foreach (var (query, freq) in output.TopQueries)
                    {
                        Console.WriteLine($"  {query}: {freq} times");
                    }
                    Console.WriteLine($"\nQuery Types:");
                    foreach (var (type, count) in output.QueryTypes)
                    {
                        Console.WriteLine($"  {type}: {count} queries");
                    }
                    Console.WriteLine($"\nAverage Response Times:");
                    foreach (var (type, time) in output.AverageResponseTimes)
                    {
                        Console.WriteLine($"  {type}: {time}");
                    }
                    Console.WriteLine($"\nLast Updated: {output.LastUpdate:yyyy-MM-dd HH:mm:ss UTC}");
                    break;
            }
        }

        internal static async Task<int> ExecuteWithOptions(CommandLineOptions options)
        {
            try
            {
                // Validate all options before proceeding
                options.Validate();

                if (options.VerbosityLevel >= 1)
                {
                    Console.WriteLine($"Analyzing code directory: {options.CodebasePath}");
                }

                var serviceProvider = ConfigureServices(options);
                var llmFactory = serviceProvider.GetRequiredService<LLMProviderFactory>();
                var provider = llmFactory.CreateProvider(options.Provider);
                var cacheProvider = serviceProvider.GetRequiredService<ICacheProvider>();

                // Initialize cache with code hash and warm up
                var codeHash = await CalculateCodeHashAsync(options.CodebasePath);
                await cacheProvider.InvalidateOnCodeChangeAsync(codeHash);
                
                var cacheSettings = serviceProvider.GetRequiredService<IOptions<CacheSettings>>().Value;
                if (cacheSettings.FrequentQuestions.Length > 0)
                {
                    var codeContext = BuildCodeContext(options.CodebasePath);
                    await cacheProvider.WarmupCacheAsync(cacheSettings.FrequentQuestions, codeContext);
                }

                if (provider is IAnthropicClient anthropicClient && !await anthropicClient.ValidateApiConnection())
                {
                    throw new Exception(
                        "Failed to validate LLM provider connection. Please check your API token and connection.");
                }

                if (options.VerbosityLevel >= 1)
                {
                    Console.WriteLine($"Using LLM Provider: {provider.ProviderName} with model: {provider.ModelName}");
                    Console.WriteLine("Configuration loaded and API connection validated successfully!");
                    
                    var cacheProvider = serviceProvider.GetRequiredService<ICacheProvider>();
                    var (hits, misses) = cacheProvider.GetCacheStats();
                    Console.WriteLine($"Cache stats - Hits: {hits}, Misses: {misses}, Hit Rate: {(hits + misses == 0 ? 0 : hits * 100.0 / (hits + misses)):F1}%");
                }

                if (options.ShowCacheStats)
                {
                    ShowCacheStats(cacheProvider, options.OutputFormat);
                    return 0;
                }
                else if (options.InteractiveMode)
                {
                    if (options.VerbosityLevel >= 1)
                    {
                        Console.WriteLine(
                            "Enter your questions about the code. Type 'exit' to quit, 'help' for commands.");
                    }

                    await StartInteractionLoop(provider, options);
                }
                else
                {
                    string codeContext = BuildCodeContext(options.CodebasePath);
                    string response = await provider.GetCompletionAsync($"I have the following code:\n\n{codeContext}\n\nMy question is: {options.DirectQuestion}");
                    OutputResponse(response, options.OutputFormat);
                }

                return 0;
            }
            catch (OptionsValidationException ex)
            {
                Console.Error.WriteLine("Configuration validation failed:");
                foreach (var failure in ex.Failures)
                {
                    Console.Error.WriteLine($"- {failure}");
                }

                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error initializing application: {ex.Message}");
                return 1;
            }
        }

        internal static void OutputResponse(string response, OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.Json:
                    var jsonObject = new {response = response};
                    var jsonOptions = new JsonSerializerOptions {WriteIndented = true};
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

        internal static async Task StartInteractionLoop(ILLMProvider provider, CommandLineOptions options)
        {
            while (_keepRunning)
            {
                Console.Write("> ");
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                try
                {
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
                                string response = await provider.GetCompletionAsync($"I have the following code:\n\n{codeContext}\n\nMy question is: {input}");
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
                            finally
                            {
                                if (options.VerbosityLevel >= 2)
                                {
                                    Console.WriteLine("Question processing completed.");
                                }
                            }
                            break;
                    }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error in command processing: {ex.Message}");
                    }
                    finally
                    {
                        if (options.VerbosityLevel >= 2)
                        {
                            Console.WriteLine("Command iteration completed.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error in interaction loop: {ex.Message}");
                    continue;
                }
                finally
                {
                    if (options.VerbosityLevel >= 2)
                    {
                        Console.WriteLine("Interaction loop iteration completed.");
                    }
                }
            }

            Console.WriteLine("Goodbye!");
        }

        internal static void ShowInteractiveHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("Help and Information:");
            Console.WriteLine("  help     - Show this help message");
            Console.WriteLine("  version  - Show version information");
            Console.WriteLine("  stats    - Show current cache statistics");
            Console.WriteLine("  history  - Show cache performance history");
            Console.WriteLine("  trends   - Show cache usage trends and recommendations");
            Console.WriteLine("  chart    - Display performance comparison charts");
            Console.WriteLine();
            Console.WriteLine("Navigation:");
            Console.WriteLine("  exit     - Exit the application");
            Console.WriteLine("  quit     - Exit the application");
            Console.WriteLine();
            Console.WriteLine("Type your questions about the code to analyze them.");
        }

        internal static void ShowHelp()
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

        internal static void ShowVersionInfo()
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

        internal static void ShowExamples()
        {
            Console.WriteLine("""
                              XPlain Usage Examples
                              ===================

                              Basic Usage
                              ----------
                              # Start interactive mode (recommended for exploration)
                              xplain ./my-project

                              # Simple code analysis
                              xplain ./my-project -q "What does this code do?"

                              Common Tasks
                              -----------
                              # Generate code documentation
                              xplain ./my-project -f markdown -q "Document the public API and main classes"

                              # Architecture analysis
                              xplain ./my-project -q "Explain the architecture and design patterns"

                              # Code review
                              xplain ./my-project -f markdown -q "Review the code for best practices"

                              # Find specific information
                              xplain ./my-project -q "List all interfaces and their implementations"

                              Output Formats
                              -------------
                              # Markdown for documentation
                              xplain ./my-project -f markdown -q "Generate API documentation"

                              # JSON for programmatic use
                              xplain ./my-project -f json -q "List all public methods"

                              # Plain text (default)
                              xplain ./my-project -q "Explain the error handling"

                              Verbosity Levels
                              ---------------
                              # Quiet mode (minimal output)
                              xplain ./my-project --verbosity 0 -q "Quick analysis"

                              # Normal mode (default)
                              xplain ./my-project -q "Standard analysis"

                              # Verbose mode (detailed output)
                              xplain ./my-project --verbosity 2 -q "Detailed analysis"

                              Advanced Usage
                              -------------
                              # Custom configuration with specific model
                              xplain ./my-project \
                                -c custom-settings.json \
                                -m claude-3-opus-20240229 \
                                -f markdown \
                                --verbosity 2 \
                                -q "Provide a comprehensive codebase analysis"

                              # Interactive mode with custom settings
                              xplain ./my-project \
                                -c review-config.json \
                                --verbosity 2 \
                                -m claude-3-opus-20240229

                              Interactive Mode Commands
                              ----------------------
                              When in interactive mode, you can use:
                              - help    : Show available commands
                              - version : Show version information
                              - exit   : Exit the application
                              - quit   : Exit the application

                              Best Practices
                              -------------
                              1. Start with interactive mode to explore the codebase
                              2. Use markdown format for documentation
                              3. Increase verbosity for detailed analysis
                              4. Ask specific, focused questions
                              5. Use environment variables for API configuration

                              For more information:
                              - Use --help for command reference
                              - Use --config-help for configuration options
                              """);
        }

        internal static void ShowConfigHelp()
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


        internal static string BuildCodeContext(string codeDirectory)
        {
            var context = new System.Text.StringBuilder();
            context.AppendLine("Code files in the directory:");

            var files = Directory.GetFiles(codeDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f) is ".cs" or ".fs" or ".vb" or ".js" or ".ts" or ".py" or ".java"
                    or ".cpp" or ".h");

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(codeDirectory, file);
                context.AppendLine($"\nFile: {relativePath}");
                context.AppendLine(File.ReadAllText(file));
            }

            return context.ToString();
        }

        internal static IServiceProvider ConfigureServices(CommandLineOptions options)
        {
            // Build configuration from various sources
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile(options.ConfigPath ?? "appsettings.override.json", optional: true)
                .AddJsonFile(
                    $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
                    optional: true)
                .AddEnvironmentVariables("XPLAIN_")
                .Build();

            // Create service collection
            IServiceCollection services = new ServiceCollection();

            // Configure cache settings
            var cacheSettings = new CacheSettings();
            configuration.GetSection("Cache").Bind(cacheSettings);
            services.AddSingleton(Options.Create(cacheSettings));
            services.AddSingleton<ICacheProvider, FileBasedCacheProvider>();

            // Configure settings
            var llmSettings = new LLMSettings();
            configuration.GetSection("LLM").Bind(llmSettings);

            // Override settings with command line options if provided
            if (!string.IsNullOrEmpty(options.Provider))
            {
                llmSettings.Provider = options.Provider;
            }
            if (!string.IsNullOrEmpty(options.ModelName))
            {
                llmSettings.Model = options.ModelName;
            }
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                llmSettings.ApiKey = options.ApiKey;
            }

            // Validate and add LLM settings
            llmSettings.Validate();
            services.AddSingleton(Options.Create(llmSettings));

            // Configure provider-specific settings based on selected provider
            switch (llmSettings.Provider.ToLowerInvariant())
            {
                case "anthropic":
                    var anthropicSettings = new AnthropicSettings
                    {
                        Provider = llmSettings.Provider,
                        Model = llmSettings.Model,
                        ApiKey = llmSettings.ApiKey,
                        ApiToken = llmSettings.ApiKey // Map LLM API key to Anthropic token
                    };
                    configuration.GetSection("Anthropic").Bind(anthropicSettings);
                    services.AddSingleton(Options.Create(anthropicSettings));
                    services.AddSingleton<IAnthropicClient, AnthropicClient>();
                    break;
                    
                default:
                    throw new InvalidOperationException($"Unsupported provider: {llmSettings.Provider}");
            }

            // Add common services
            services.AddHttpClient();
            services.AddSingleton<LLMProviderFactory>();

            return services.BuildServiceProvider();
        }
    }
}