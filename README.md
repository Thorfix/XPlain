# XPlain
A tool to explain codebases using LLM (Anthropic's Claude Sonnet 3.5)

## Overview
XPlain is a local development tool designed to help developers understand codebases by allowing them to ask questions about code in a designated folder. The tool uses Anthropic's Claude Sonnet 3.5 to provide intelligent responses based on the codebase contents.

## Features
- Simple CLI interface for asking questions about your code
- Integration with Anthropic's Claude Sonnet 3.5
- Configuration system for API tokens and settings
- Tool-assisted code analysis capabilities
- Windows-focused development (though may work on other platforms)

## Project Structure

```
├── src/
│   └── XPlain/           # Main project directory
│       ├── Program.cs    # Entry point
│       └── XPlain.csproj # Project file
├── .gitignore           # Git ignore file
├── README.md            # This file
└── XPlain.sln          # Solution file
```

## Requirements
- .NET 8 SDK
- Anthropic API token

## Getting Started
1. Configure your Anthropic API token using one of these methods:
   - Set it in `appsettings.json`:
     ```json
     {
       "Anthropic": {
         "ApiToken": "your-api-token-here"
       }
     }
     ```
   - Set the environment variable: `THORFIX_ANTHROPIC__APITOKEN`
2. Other settings can be configured in `appsettings.json` or via environment variables:
   - `ApiEndpoint`: The Anthropic API endpoint (default: https://api.anthropic.com/v1)
   - `MaxTokenLimit`: Maximum token limit for requests (default: 2000)
   - `DefaultModel`: The model to use (default: claude-2)
3. Run the tool from the command line
4. Point to your code directory
5. Ask questions about your codebase

### Configuration
The application uses a flexible configuration system that supports:
- JSON configuration via `appsettings.json`
- Environment variables (prefixed with `THORFIX_`)
- Validation for required settings
- Secure handling of sensitive data

Environment variables use double underscore to represent nested settings:
```
THORFIX_ANTHROPIC__APITOKEN=your-token-here
THORFIX_ANTHROPIC__APIENDPOINT=https://custom-endpoint
THORFIX_ANTHROPIC__MAXTOKENLIMIT=4000
THORFIX_ANTHROPIC__DEFAULTMODEL=claude-2
```

### Building the Project

```bash
dotnet build
```

### Running the Project

```bash
dotnet run --project src/XPlain/XPlain.csproj
```

### Command-Line Interface

XPlain provides a comprehensive command-line interface with grouped options for better organization and usability.

```
USAGE:
  xplain <codebase-path> [options]

ARGUMENTS:
  codebase-path    Path to the codebase directory to analyze (required)

HELP AND INFORMATION:
  -h, --help       Show help message and exit
  -v, --version    Display version information
  --examples       Show usage examples
  --config-help    Display configuration options and environment variables

EXECUTION MODE OPTIONS:
  -q, --question   Direct question to ask about the code (skips interactive mode)

OUTPUT CONFIGURATION:
  --verbosity <n>  Verbosity level (0=quiet, 1=normal, 2=verbose)
  -f, --format     Output format (text, json, or markdown)

MODEL CONFIGURATION:
  -c, --config     Path to custom configuration file
  -m, --model      Override the AI model to use (must start with 'claude-')

VALIDATION:
- Codebase path must exist
- Verbosity level must be between 0 and 2
- Configuration file must exist if specified
- Model name must start with 'claude-' if specified
```

#### Help Commands

1. General Help (`--help` or `-h`):
   - Shows overall usage information
   - Lists all available options with descriptions
   - Displays argument requirements

2. Version Information (`--version` or `-v`):
   - Shows current version of XPlain
   - Displays build information

3. Usage Examples (`--examples`):
   ```bash
   # Basic Interactive Mode
   xplain ./my-project

   # Direct Question with Markdown Output
   xplain ./my-project -f markdown -q "What does Program.cs do?"

   # Detailed Analysis with Custom Model
   xplain ./my-project --verbosity 2 -m claude-3-opus-20240229 \
     -q "Analyze the architecture"

   # Custom Configuration with JSON Output
   xplain ./my-project -c custom-settings.json -f json \
     -q "List all classes"
   ```

4. Configuration Help (`--config-help`):
   ```bash
   # Environment Variables
   XPLAIN_ANTHROPIC__APITOKEN=your-token-here
   XPLAIN_ANTHROPIC__APIENDPOINT=https://api.anthropic.com/v1
   XPLAIN_ANTHROPIC__MAXTOKENLIMIT=4000
   XPLAIN_ANTHROPIC__DEFAULTMODEL=claude-2

   # Configuration File (appsettings.json)
   {
     "Anthropic": {
       "ApiToken": "your-api-token-here",
       "ApiEndpoint": "https://api.anthropic.com/v1",
       "MaxTokenLimit": 2000,
       "DefaultModel": "claude-2"
     }
   }
   ```

#### Interactive Mode Commands

When running in interactive mode, the following commands are available:
```
help     - Show interactive mode help
version  - Show version information
exit     - Exit the application
quit     - Exit the application
```

Type your questions directly at the prompt to analyze the codebase.

## Project Status
This is an initial implementation focused on core functionality. Testing is currently out of scope, meaning this project should not contain any tests of any kind.

## To Do
- [x] Set up basic project structure
- [ ] Implement configuration system
  - [ ] Add Anthropic API token configuration 
  - [ ] Add other necessary settings
- [ ] Create CLI interface
  - [ ] Command line argument parsing
  - [ ] User interaction flow
- [ ] Implement LLM integration
  - [ ] Anthropic API client
  - [ ] Tool calls for code analysis
  - [ ] Response formatting
- [ ] Add code analysis capabilities
  - [ ] File system navigation
  - [ ] Code reading and parsing
  - [ ] Context building for LLM

## Contributing
This is a focused tool with a specific purpose. Contributions should align with the goal of keeping the implementation as simple as possible while maintaining functionality.
