using System;

namespace XPlain.Services
{
    public class LLMProviderException : Exception
    {
        public LLMErrorType ErrorType { get; }
        public bool IsTransient { get; }

        public LLMProviderException(string message, LLMErrorType errorType, bool isTransient, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorType = errorType;
            IsTransient = isTransient;
        }
    }

    public enum LLMErrorType
    {
        // Authentication/Authorization errors
        InvalidApiKey,
        Unauthorized,
        
        // Rate limiting errors
        RateLimitExceeded,
        QuotaExceeded,
        ConcurrentRequestLimitExceeded,
        
        // Network/Infrastructure errors
        Timeout,
        NetworkError,
        ServiceUnavailable,
        
        // Input/Processing errors
        InvalidInput,
        ContextLengthExceeded,
        ContentFiltered,
        
        // Provider-specific errors
        ModelNotAvailable,
        InvalidModel,
        
        // Unknown/Other
        Unknown
    }
}