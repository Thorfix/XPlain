using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Thorfix.Configuration;

namespace Thorfix.Services;

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
                await Task.Delay(MinRequestInterval - (int)timeSinceLastRequest.TotalMilliseconds);
            }

            var prompt = BuildPrompt(question, codeContext);
            var requestBody = new
            {
                model = _settings.DefaultModel,
                prompt = prompt,
                max_tokens_to_sample = _settings.MaxTokenLimit,
                temperature = 0.7,
                stop_sequences = new[] { "\n\nHuman:", "\n\nAssistant:" }
            };

            var response = await _httpClient.PostAsJsonAsync("/complete", requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>();
            _lastRequestTime = DateTime.UtcNow;

            return result?.Completion?.Trim() ?? "No response received from the API.";
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
            var requestBody = new
            {
                model = _settings.DefaultModel,
                prompt = "\n\nHuman: Hello\n\nAssistant:",
                max_tokens_to_sample = 1,
                temperature = 0
            };

            var response = await _httpClient.PostAsJsonAsync("/complete", requestBody);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string BuildPrompt(string question, string codeContext)
    {
        return $"\n\nHuman: I have the following code:\n\n{codeContext}\n\nMy question is: {question}\n\nAssistant:";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _rateLimiter.Dispose();
    }
}

private class AnthropicResponse
{
    public string? Completion { get; set; }
    public string? Stop { get; set; }
    public string? Model { get; set; }
}