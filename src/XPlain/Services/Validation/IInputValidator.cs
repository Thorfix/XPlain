using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    public interface IInputValidator
    {
        /// <summary>
        /// Validates and sanitizes input text before sending to LLM provider
        /// </summary>
        /// <param name="input">The raw input text</param>
        /// <param name="providerName">The name of the LLM provider</param>
        /// <returns>Sanitized input text</returns>
        /// <exception cref="InputValidationException">Thrown when input fails validation</exception>
        Task<string> ValidateAndSanitizeAsync(string input, string providerName);
    }
}