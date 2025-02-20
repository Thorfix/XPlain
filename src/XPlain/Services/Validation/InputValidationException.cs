using System;

namespace XPlain.Services.Validation
{
    public class InputValidationException : Exception
    {
        public string ProviderType { get; }
        public string ValidationError { get; }

        public InputValidationException(string message, string providerType, string validationError) 
            : base(message)
        {
            ProviderType = providerType;
            ValidationError = validationError;
        }

        public InputValidationException(string message, string providerType, string validationError, Exception innerException)
            : base(message, innerException)
        {
            ProviderType = providerType;
            ValidationError = validationError;
        }
    }
}