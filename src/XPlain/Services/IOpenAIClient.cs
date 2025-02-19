using System.Threading.Tasks;

namespace XPlain.Services;

/// <summary>
/// Interface for OpenAI API client operations
/// </summary>
public interface IOpenAIClient
{
    /// <summary>
    /// Sends a completion request to the OpenAI API
    /// </summary>
    /// <param name="prompt">The prompt to send</param>
    /// <returns>The completion response</returns>
    Task<string> GetCompletionAsync(string prompt);
}