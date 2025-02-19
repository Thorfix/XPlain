using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services;

public class AnthropicClient : IAnthropicClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicSettings _settings;
    private readonly SemaphoreSlim _rateLimiter;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestInterval = 1000; // 1 second between requests for rate limiting

    public AnthropicClient(IOptions<AnthropicSettings> settings)
    {
        _settings = settings.Value;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.ApiEndpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _settings.ApiToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _rateLimiter = new SemaphoreSlim(1, 1);
    }

    public async Task<string> AskQuestion(string question, string codeContext)
    {
        await _rateLimiter.WaitAsync();
        try
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest.TotalMilliseconds < MinRequestInterval)
            {
                await Task.Delay(MinRequestInterval - (int) timeSinceLastRequest.TotalMilliseconds);
            }

            var prompt = BuildPrompt(question, codeContext);
            AnthropicRequest requestBody = new AnthropicRequest
            {
                Model = _settings.DefaultModel,
                Messages =
                [
                    new AnthropicMessage
                        {Role = "user", Content = [new AnthropicMessageContent {Type = "text", Text = prompt}]}
                ],
                MaxTokens = _settings.MaxTokenLimit,
                Temperature = 0.7,
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>();
            _lastRequestTime = DateTime.UtcNow;

            return result?.Content.FirstOrDefault()?.Text.Trim() ?? "No response received from the API.";
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Failed to communicate with Anthropic API: {ex.Message}", ex);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task<bool> ValidateApiConnection()
    {
        try
        {
            // Simple validation request with minimal tokens
            AnthropicRequest requestBody = new AnthropicRequest
            {
                Model = _settings.DefaultModel,
                MaxTokens = 1,
                Messages =
                [
                    new AnthropicMessage()
                        {Role = "user", Content = [new AnthropicMessageContent {Type = "text", Text = "Hello"}]}
                ],
                Temperature = 0
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", requestBody);
            Debug.WriteLine(await response.Content.ReadAsStringAsync());
            return response.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return false;
        }
    }

    private string BuildPrompt(string question, string codeContext)
    {
        return $"\n\nI have the following code:\n\n{codeContext}\n\nMy question is: {question}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _rateLimiter.Dispose();
    }
}

public class AnthropicRequest
{
    [JsonPropertyName("model")] public required string Model { get; set; }
    [JsonPropertyName("max_tokens")] public required int MaxTokens { get; set; }
    [JsonPropertyName("messages")] public required List<AnthropicMessage> Messages { get; set; }
    [JsonPropertyName("temperature")] public double? Temperature { get; set; }
    [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
}

public class AnthropicMessage
{
    [JsonPropertyName("role")] public required string Role { get; set; }
    [JsonPropertyName("content")] public required List<AnthropicMessageContent> Content { get; set; }
}

public class AnthropicMessageContent
{
    [JsonPropertyName("type")] public required string Type { get; set; } = "text";
    [JsonPropertyName("text")] public required string Text { get; set; }
}

public class AnthropicResponse
{
    [JsonPropertyName("id")] public required string Id { get; set; }
    [JsonPropertyName("model")] public required string Model { get; set; }
    [JsonPropertyName("role")] public required string Role { get; set; } = "assistant";
    [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
    [JsonPropertyName("stop_sequence")] public string? StopSequence { get; set; }
    [JsonPropertyName("content")] public required List<AnthropicMessageContent> Content { get; set; }
}