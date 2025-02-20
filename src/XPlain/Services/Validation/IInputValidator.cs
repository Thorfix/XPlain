using System;
using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    public interface IInputValidator
    {
        /// <summary>
        /// Validates and sanitizes the input prompt.
        /// </summary>
        /// <param name="prompt">The input prompt to validate and sanitize</param>
        /// <param name="providerType">The type of LLM provider</param>
        /// <returns>The sanitized prompt if valid</returns>
        /// <throws>InputValidationException if the input is invalid</throws>
        Task<string> ValidateAndSanitizeAsync(string prompt, string providerType);

        /// <summary>
        /// Checks if the input length is within provider-specific limits
        /// </summary>
        bool IsLengthValid(string prompt, string providerType);

        /// <summary>
        /// Validates the input against security rules to prevent prompt injection
        /// </summary>
        bool ValidateSecurityRules(string prompt);

        /// <summary>
        /// Normalizes character encoding and removes potentially harmful characters
        /// </summary>
        string SanitizeInput(string prompt);
    }
}