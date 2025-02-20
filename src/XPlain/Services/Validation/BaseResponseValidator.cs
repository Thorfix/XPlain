using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Base implementation of response validation with common validation logic
    /// </summary>
    public abstract class BaseResponseValidator : IResponseValidator
    {
        /// <summary>
        /// Validates if the response follows proper JSON schema when applicable
        /// </summary>
        public virtual async Task<bool> ValidateSchemaAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            try
            {
                if (response.StartsWith("{") || response.StartsWith("["))
                {
                    JsonDocument.Parse(response);
                }
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks response quality metrics like length, completeness, etc.
        /// </summary>
        public virtual async Task<bool> ValidateQualityAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            // Basic quality checks
            if (response.Length < 10) // Minimum reasonable response length
                return false;

            if (response.Contains("error", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Provider-specific format validation
        /// </summary>
        public abstract Task<bool> ValidateFormatAsync(string response);

        /// <summary>
        /// Checks for common error patterns
        /// </summary>
        public virtual async Task<bool> DetectErrorsAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            // Common error patterns
            if (response.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("api key", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}