namespace Thorfix.Services;

public interface IAnthropicClient
{
    /// <summary>
    /// Asks a question about the code using the Anthropic API
    /// </summary>
    /// <param name="question">The question to ask</param>
    /// <param name="codeContext">The relevant code context for the question</param>
    /// <returns>The response from the LLM</returns>
    Task<string> AskQuestion(string question, string codeContext);

    /// <summary>
    /// Validates the connection to the Anthropic API
    /// </summary>
    /// <returns>True if the connection is valid, false otherwise</returns>
    Task<bool> ValidateApiConnection();
}