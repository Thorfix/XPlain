# XPlain
An AI-powered code analysis tool supporting multiple LLM providers

## Overview
XPlain is a local development tool designed to help developers understand codebases by allowing them to ask questions about code in a designated folder. The tool supports multiple LLM providers to provide intelligent responses based on the codebase contents. Currently supported providers:

- **Anthropic Claude** (default)
  - Models: claude-3-opus-20240229, claude-3-sonnet-20240229
  - Features: High-quality code analysis, contextual understanding
  - Configuration: API key required

- **OpenAI**
  - Models: gpt-4-turbo-preview, gpt-3.5-turbo
  - Features: Fast responses, broad knowledge base
  - Configuration: API key required

Additional providers can be added by implementing the `ILLMProvider` interface and following the provider integration guidelines.

## Features
- Simple CLI interface for asking questions about your code
- Support for multiple LLM providers (Anthropic Claude and OpenAI)
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
- Anthropic API token or OpenAI API key

## Getting Started
1. Configure your LLM provider and API token using one of these methods:
   - Set it in `appsettings.json`:
     ```json
     {
       "LLM": {
         "Provider": "Anthropic",  // or "OpenAI"
         "Model": "claude-3-sonnet-20240229",  // or "gpt-4-turbo-preview"
         "ApiKey": "your-api-key-here"
       },
       "Anthropic": {
         "ApiToken": "your-api-token-here"
       },
       "OpenAI": {
         "ApiToken": "your-openai-key-here"
       }
     }
     ```
   - Set the environment variable: 
     - For Anthropic: `THORFIX_ANTHROPIC__APITOKEN`
     - For OpenAI: `THORFIX_OPENAI__APITOKEN`
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

### Command-Line Interface

XPlain provides a comprehensive command-line interface designed to be user-friendly and intuitive. The interface follows standard CLI conventions and includes detailed help and documentation.

The CLI is organized into logical option groups for better organization and understanding:

1. Required Options
2. Execution Mode Options
3. Output Configuration Options
4. Model Configuration Options
5. Help and Information Options

#### Basic Usage

```bash
# Start interactive mode (basic usage)
xplain ./my-project

# Ask a direct question
xplain ./my-project --question "What does this code do?"

# Get detailed analysis in markdown format
xplain ./my-project --format markdown --verbosity 2

# Advanced usage with custom configuration
xplain ./my-project \
  --config custom-settings.json \
  --model claude-3-opus-20240229 \
  --format markdown \
  --verbosity 2 \
  --question "Provide a comprehensive codebase analysis"
```

#### Help Commands and Documentation

XPlain provides several help commands to assist users:

1. **General Help** (`--help` or `-h`)
   ```bash
   xplain --help
   ```
   Shows complete usage information, available options, and basic examples.

2. **Version Information** (`--version` or `-v`)
   ```bash
   xplain --version
   ```
   Displays version number and build information.

3. **Usage Examples** (`--examples`)
   ```bash
   xplain --examples
   ```
   Shows comprehensive examples covering common use cases.

4. **Configuration Help** (`--config-help`)
   ```bash
   xplain --config-help
   ```
   Explains configuration options and environment variables.

#### Command-Line Options

Options are organized into logical groups for better understanding:

1. **Required Options**
   - `<codebase-path>`
     - Path to the code directory to analyze
     - Must be a valid directory
     - Example: `xplain ./my-project`

2. **Execution Mode Options**
   - `--question, -q <question>`
     - Direct question about the code
     - Must be at least 10 characters
     - Example: `--question "What does Program.cs do?"`

3. **Output Configuration**
   - `--verbosity <level>`
     - `0`: Quiet (minimal output)
     - `1`: Normal (default)
     - `2`: Verbose (detailed output)
     - Example: `--verbosity 2`
   
   - `--format, -f <format>`
     - Output format options:
       - `text`: Plain text (default)
       - `json`: JSON format
       - `markdown`: Markdown format
     - Example: `--format markdown`

4. **Model Configuration**
   - `--config, -c <path>`
     - Path to custom JSON configuration
     - Example: `--config custom-settings.json`
   
   - `--model, -m <model>`
     - Override AI model version
     - Must start with 'claude-'
     - Example: `--model claude-3-opus-20240229`

#### Validation Rules

The CLI includes helpful validation to prevent errors:

1. **Codebase Path**
   - Must be specified
   - Directory must exist
   - Error: "Codebase path is required. Please specify the directory containing your code"

2. **Question Format**
   - Minimum 10 characters
   - Error includes example of proper format

3. **Verbosity Level**
   - Must be between 0 and 2
   - Error shows valid range and example

4. **Output Format**
   - Must be one of: Text, Json, Markdown
   - Case-insensitive
   - Error shows valid options

5. **Configuration File**
   - Must be a .json file
   - Must exist if specified
   - Error includes example path

6. **Model Name**
   - Must start with 'claude-'
   - Error shows correct format

#### Environment Variables

Configure XPlain using environment variables:
```bash
# Required
XPLAIN_ANTHROPIC__APITOKEN=your-token-here

# Optional
XPLAIN_ANTHROPIC__APIENDPOINT=https://api.anthropic.com/v1
XPLAIN_ANTHROPIC__MAXTOKENLIMIT=4000
XPLAIN_ANTHROPIC__DEFAULTMODEL=claude-2
```

#### Examples of Common Use Cases

1. **Basic Analysis**
   ```bash
   xplain ./my-project
   ```

2. **Quick Question**
   ```bash
   xplain ./my-project -q "What are the main classes?" -f text
   ```

3. **Detailed Documentation**
   ```bash
   xplain ./my-project --verbosity 2 -f markdown \
     -q "Provide a detailed analysis of the architecture"
   ```

4. **Custom Configuration**
   ```bash
   xplain ./my-project -c custom-config.json \
     -m claude-3-opus-20240229 --format json
   ```

5. **Quiet Mode**
   ```bash
   xplain ./my-project --verbosity 0 -q "List all public methods"
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
- [x] Implement configuration system
  - [x] Add Anthropic API token configuration 
  - [x] Add other necessary settings
- [x] Create CLI interface
  - [x] Command line argument parsing
  - [x] User interaction flow
  - [x] Comprehensive help system
  - [x] Option validation and error handling
- [x] Implement LLM integration
  - [x] Anthropic API client
  - [x] Tool calls for code analysis
  - [x] Response formatting
- [x] Add code analysis capabilities
  - [x] File system navigation
  - [x] Code reading and parsing
  - [x] Context building for LLM

## Contributing
This is a focused tool with a specific purpose. Contributions should align with the goal of keeping the implementation as simple as possible while maintaining functionality.
