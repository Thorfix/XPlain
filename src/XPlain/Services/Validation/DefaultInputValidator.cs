using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace XPlain.Services.Validation
{
    public class DefaultInputValidator : IInputValidator
    {
        private readonly Dictionary<string, int> _providerMaxTokens = new()
        {
            { "OpenAI", 4096 },
            { "Anthropic", 100000 },
            { "AzureOpenAI", 4096 }
        };

        private readonly string[] _forbiddenPatterns = new[]
        {
            @"<script.*?>.*?</script>",
            @"{{.*?}}",
            @"\{\{.*?\}\}",
            @"system:\s*prompt",
            @"assistant:\s*prompt"
        };

        public async Task<string> ValidateAndSanitizeAsync(string prompt, string providerType)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                throw new InputValidationException("Prompt cannot be empty", providerType, "EmptyPrompt");
            }

            if (!IsLengthValid(prompt, providerType))
            {
                throw new InputValidationException(
                    $"Prompt exceeds maximum length for provider {providerType}", 
                    providerType, 
                    "MaxLengthExceeded"
                );
            }

            if (!ValidateSecurityRules(prompt))
            {
                throw new InputValidationException(
                    "Prompt contains potentially malicious content", 
                    providerType, 
                    "SecurityValidationFailed"
                );
            }

            return SanitizeInput(prompt);
        }

        public bool IsLengthValid(string prompt, string providerType)
        {
            if (!_providerMaxTokens.TryGetValue(providerType, out int maxTokens))
            {
                // Default to most restrictive if provider not found
                maxTokens = 4096;
            }

            // Rough estimation: 1 token ≈ 4 characters
            return prompt.Length <= maxTokens * 4;
        }

        public bool ValidateSecurityRules(string prompt)
        {
            foreach (var pattern in _forbiddenPatterns)
            {
                if (Regex.IsMatch(prompt, pattern, RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }

            // Check for common injection patterns
            if (prompt.Contains("```") || prompt.Contains("'''"))
            {
                var count = Regex.Matches(prompt, @"```|'''").Count;
                if (count % 2 != 0) return false; // Unmatched code blocks
            }

            return true;
        }

        public string SanitizeInput(string prompt)
        {
            // Normalize to NFKC form
            prompt = prompt.Normalize(NormalizationForm.FormKC);

            // Remove control characters except newlines and tabs
            var sb = new StringBuilder();
            foreach (char c in prompt)
            {
                if (!char.IsControl(c) || c == '\n' || c == '\t')
                {
                    sb.Append(c);
                }
            }

            // Remove null bytes
            prompt = sb.ToString().Replace("\0", "");

            // Trim excessive whitespace
            prompt = Regex.Replace(prompt, @"\s+", " ").Trim();

            return prompt;
        }
    }
}