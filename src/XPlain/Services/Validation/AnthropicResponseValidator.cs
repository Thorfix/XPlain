using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Validator for Anthropic API responses
    /// </summary>
    public class AnthropicResponseValidator : BaseResponseValidator
    {
        /// <summary>
        /// Validates Anthropic-specific response format
        /// </summary>
        public override async Task<bool> ValidateFormatAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            // Anthropic-specific format checks
            // Check for completion format and typical response patterns
            if (!response.Contains("Human:") && !response.Contains("Assistant:"))
                return false;

            // Additional Anthropic-specific validations can be added here
            return true;
        }
    }
}