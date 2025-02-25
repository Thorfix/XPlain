using System;

namespace XPlain.Services.Validation
{
    public enum ValidationErrorType
    {
        EmptyInput,
        InvalidContent,
        TooLong,
        ProhibitedContent,
        Malformed
    }

    public class InputValidationException : Exception
    {
        public ValidationErrorType ValidationError { get; }

        public InputValidationException(string message, ValidationErrorType validationError)
            : base(message)
        {
            ValidationError = validationError;
        }

        public InputValidationException(string message, ValidationErrorType validationError, Exception innerException)
            : base(message, innerException)
        {
            ValidationError = validationError;
        }
    }
}