using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class AzureOpenAIClient : BaseLLMProvider, IAzureOpenAIClient
    {
        private readonly HttpClient _httpClient;
        private readonly AzureOpenAISettings _settings;

        public string Endpoint => _settings.Endpoint;
        public string DeploymentId => _settings.DeploymentId;
        public string ApiVersion => _settings.ApiVersion;

        public AzureOpenAIClient(
            IOptions<AzureOpenAISettings> settings,
            HttpClient httpClient,
            IOptions<RateLimitSettings> rateLimitSettings,
            IRateLimitingService rateLimitingService)
            : base(rateLimitSettings?.Value, rateLimitingService)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            
            _httpClient.BaseAddress = new Uri(_settings.Endpoint);
            _httpClient.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);
        }

        public async Task<string> CompletePromptAsync(string prompt)
        {
            await EnforceRateLimits();

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 2000
            };

            var url = $"/openai/deployments/{_settings.DeploymentId}/chat/completions?api-version={_settings.ApiVersion}";
            
            var response = await _httpClient.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            );

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(content);
            
            return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        public async Task<string> StreamCompletionAsync(string prompt)
        {
            await EnforceRateLimits();

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 2000,
                stream = true
            };

            var url = $"/openai/deployments/{_settings.DeploymentId}/chat/completions?api-version={_settings.ApiVersion}";
            
            var response = await _httpClient.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            );

            response.EnsureSuccessStatusCode();
            
            var streamContent = await response.Content.ReadAsStringAsync();
            return streamContent; // In a real implementation, this would handle SSE parsing
        }
    }
}