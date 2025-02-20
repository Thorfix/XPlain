using System.Text.Json;
using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Validator for OpenAI API responses
    /// </summary>
    public class OpenAIResponseValidator : BaseResponseValidator
    {
        /// <summary>
        /// Validates OpenAI-specific response format
        /// </summary>
        public override async Task<bool> ValidateFormatAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            try
            {
                // OpenAI responses are typically JSON with specific fields
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                // Check for typical OpenAI response fields
                if (!root.TryGetProperty("choices", out var choices))
                    return false;

                if (!choices.EnumerateArray().Any())
                    return false;

                // Additional OpenAI-specific validations can be added here
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}