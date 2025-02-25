using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ILLMProvider
    {
        /// <summary>
        /// Name of the provider (e.g., "Anthropic", "OpenAI")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Name of the model being used (e.g., "claude-3-opus-20240229", "gpt-4-turbo-preview")
        /// </summary>
        string ModelName { get; }

        /// <summary>
        /// Checks if the provider is currently healthy and available
        /// </summary>
        bool IsHealthy();

        /// <summary>
        /// Gets a completion from the LLM for the given prompt
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM</param>
        /// <returns>The LLM's response</returns>
        Task<string> GetCompletionAsync(string prompt);

        /// <summary>
        /// Gets a streaming completion from the LLM for the given prompt
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM</param>
        /// <returns>An async enumerable of response chunks</returns>
        IAsyncEnumerable<string> GetCompletionStreamAsync(string prompt);
    }
}