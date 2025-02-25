using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IAnthropicClient : ILLMProvider
    {
        /// <summary>
        /// Validates the API connection to Anthropic
        /// </summary>
        /// <returns>True if the connection is valid, false otherwise</returns>
        Task<bool> ValidateApiConnection();
    }
}