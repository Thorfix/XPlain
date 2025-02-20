using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ILLMProvider
    {
        /// <summary>
        /// Gets a completion from the LLM provider
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM</param>
        /// <returns>The completion response from the LLM</returns>
        Task<string> GetCompletionAsync(string prompt);

        /// <summary>
        /// Gets a streaming completion from the LLM provider
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM</param>
        /// <returns>An async enumerable of completion chunks from the LLM</returns>
        Task<IAsyncEnumerable<string>> GetCompletionStreamAsync(string prompt);

        /// <summary>
        /// Gets the name of the LLM provider
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Gets the model being used by this provider
        /// </summary>
        string ModelName { get; }
    }
}