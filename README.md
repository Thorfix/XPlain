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

## Requirements
- .NET 8 SDK
- Anthropic API token

## Getting Started
1. Configure your Anthropic API token in the configuration file
2. Run the tool from the command line
3. Point to your code directory
4. Ask questions about your codebase

## Project Status
This is an initial implementation focused on core functionality. Testing is currently out of scope.

## To Do
- [ ] Set up basic project structure
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
