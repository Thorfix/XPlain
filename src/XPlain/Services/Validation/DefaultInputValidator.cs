using System;
using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    public class DefaultInputValidator : IInputValidator
    {
        public Task<string> ValidateAndSanitizeAsync(string input, string providerName)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new InputValidationException(
                    "Input cannot be empty",
                    ValidationErrorType.EmptyInput);
            }

            // Simple input validation and sanitization
            // Could be expanded with more sophisticated checks
            var sanitized = input
                .Replace("\0", "")  // Remove null characters
                .Trim();            // Trim whitespace

            if (string.IsNullOrEmpty(sanitized))
            {
                throw new InputValidationException(
                    "Input contains only whitespace or invalid characters",
                    ValidationErrorType.InvalidContent);
            }

            return Task.FromResult(sanitized);
        }
    }
}