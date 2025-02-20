using System.Threading.Tasks;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Interface for validating LLM API responses
    /// </summary>
    public interface IResponseValidator
    {
        /// <summary>
        /// Validates the schema and structure of the response
        /// </summary>
        /// <param name="response">The response content to validate</param>
        /// <returns>True if the response schema is valid, false otherwise</returns>
        Task<bool> ValidateSchemaAsync(string response);

        /// <summary>
        /// Checks the quality of the response content
        /// </summary>
        /// <param name="response">The response content to validate</param>
        /// <returns>True if the response quality meets requirements, false otherwise</returns>
        Task<bool> ValidateQualityAsync(string response);

        /// <summary>
        /// Verifies the response format matches provider-specific requirements
        /// </summary>
        /// <param name="response">The response content to validate</param>
        /// <returns>True if the format is valid, false otherwise</returns>
        Task<bool> ValidateFormatAsync(string response);

        /// <summary>
        /// Detects any error patterns or invalid content in the response
        /// </summary>
        /// <param name="response">The response content to validate</param>
        /// <returns>True if no errors are detected, false otherwise</returns>
        Task<bool> DetectErrorsAsync(string response);
    }
}