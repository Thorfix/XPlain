namespace XPlain.Services
{
    public enum LLMErrorType
    {
        Unknown,
        Unauthorized,
        RateLimitExceeded,
        Timeout,
        ServiceUnavailable,
        InvalidInput
    }
}