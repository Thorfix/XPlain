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
