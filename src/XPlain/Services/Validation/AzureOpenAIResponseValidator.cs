using System.Text.Json;
using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Validator for Azure OpenAI API responses
    /// </summary>
    public class AzureOpenAIResponseValidator : BaseResponseValidator
    {
        /// <summary>
        /// Validates Azure OpenAI-specific response format
        /// </summary>
        public override async Task<bool> ValidateFormatAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            try
            {
                // Azure OpenAI follows similar format to OpenAI
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                // Check for typical Azure OpenAI response fields
                if (!root.TryGetProperty("choices", out var choices))
                    return false;

                if (!choices.EnumerateArray().Any())
                    return false;

                // Check for Azure-specific fields
                if (root.TryGetProperty("id", out var id) && 
                    !id.GetString().StartsWith("azure"))
                    return false;

                // Additional Azure OpenAI-specific validations can be added here
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}