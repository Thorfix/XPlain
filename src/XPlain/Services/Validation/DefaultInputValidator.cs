using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace XPlain.Services.Validation
{
    public class InputValidationException : Exception
    {
        public ValidationErrorType ValidationError { get; }

        public InputValidationException(string message, ValidationErrorType errorType)
            : base(message)
        {
            ValidationError = errorType;
        }
    }

    public class DefaultInputValidator : IInputValidator
    {
        private readonly int _maxInputLength = 100000; // Default max length

        public Task<string> ValidateAndSanitizeAsync(string input, string providerName)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new InputValidationException(
                    "Input cannot be empty",
                    ValidationErrorType.EmptyInput);
            }

            // Check length limits
            if (input.Length > _maxInputLength)
            {
                throw new InputValidationException(
                    $"Input exceeds maximum length of {_maxInputLength} characters",
                    ValidationErrorType.TooLong);
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

            // Check for excessive special character sequences
            if (ContainsExcessiveSpecialCharacters(sanitized))
            {
                throw new InputValidationException(
                    "Input contains excessive special character sequences",
                    ValidationErrorType.ExcessiveSpecialChars);
            }

            return Task.FromResult(sanitized);
        }

        private bool ContainsExcessiveSpecialCharacters(string input)
        {
            // Check for repeating patterns of special characters that might indicate an attack
            var repeatingSpecialChars = new Regex(@"([^\w\s])\1{10,}");
            return repeatingSpecialChars.IsMatch(input);
        }
    }
}