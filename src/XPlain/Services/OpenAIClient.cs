using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services;

/// <summary>
/// OpenAI API client implementation
/// </summary>
public class OpenAIClient : IOpenAIClient, ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenAISettings _settings;

    public OpenAIClient(IOptions<OpenAISettings> settings)
    {
        _settings = settings.Value;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.ApiEndpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiToken}");
    }

    public async Task<string> GetCompletionAsync(string prompt)
    {
        var requestBody = new
        {
            model = _settings.DefaultModel,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = _settings.MaxTokenLimit
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var responseData = JsonSerializer.Deserialize<JsonElement>(responseString);
        
        return responseData
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    public async Task<string> ProcessPromptAsync(string prompt)
    {
        return await GetCompletionAsync(prompt);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}