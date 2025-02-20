using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Net;

namespace XPlain.Services;

public class AnthropicClient : BaseLLMProvider, IAnthropicClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicSettings _settings;
    private readonly IRateLimitingService _rateLimitingService;
    private readonly CircuitBreaker _circuitBreaker;
    private const int RequestTimeoutMs = 30000;
    
    public override string ProviderName => "Anthropic";
    public override string ModelName => _settings.DefaultModel;

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        HttpStatusCode.BadGateway,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.RequestTimeout
    };

    public AnthropicClient(
        IOptions<AnthropicSettings> settings,
        ICacheProvider cacheProvider,
        IRateLimitingService rateLimitingService)
        : base(cacheProvider, rateLimitingService)
    {
        _settings = settings.Value;
        _rateLimitingService = rateLimitingService;
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.ApiEndpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _settings.ApiToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        _circuitBreaker = new CircuitBreaker(
            _settings.CircuitBreakerFailureThreshold,
            _settings.CircuitBreakerResetTimeoutMs);
    }

    protected override async Task<string> GetCompletionInternalAsync(string prompt)
    {
        var requestBody = new AnthropicRequest
        {
            Model = _settings.DefaultModel,
            Messages = [
                new AnthropicMessage
                {
                    Role = "user",
                    Content = [new AnthropicMessageContent { Type = "text", Text = prompt }]
                }
            ],
            MaxTokens = _settings.MaxTokenLimit,
            Temperature = 0.7
        };

        return await ExecuteWithRetryAsync(async (ct) =>
        {
            var response = await ProcessRequestAsync(requestBody, ct);
            return response?.Content.FirstOrDefault()?.Text.Trim() ?? 
                   "No response received from the API.";
        }, 0);
    }

    private async Task<AnthropicResponse> ProcessRequestAsync(
        AnthropicRequest request, 
        CancellationToken cancellationToken)
    {
        // Acquire rate limiting permit
        await _rateLimitingService.AcquirePermitAsync(ProviderName, 0, cancellationToken);
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/v1/messages",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AnthropicResponse>(
                cancellationToken: cancellationToken);
        }
        finally
        {
            _rateLimitingService.ReleasePermit(ProviderName);
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanProcess())
        {
            throw new CircuitBreakerOpenException(
                "Circuit breaker is open. Too many recent failures.");
        }

        var attempts = 0;
        var delay = _settings.InitialRetryDelayMs;
        Exception lastException = null;

        while (attempts < _settings.MaxRetryAttempts)
        {
            try
            {
                attempts++;
                var result = await operation(cancellationToken);
                _circuitBreaker.RecordSuccess();
                return result;
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode.HasValue && 
                RetryableStatusCodes.Contains(ex.StatusCode.Value))
            {
                lastException = ex;
                _circuitBreaker.RecordFailure();

                if (attempts < _settings.MaxRetryAttempts)
                {
                    // Add jitter to delay
                    var jitter = Random.Shared.NextDouble() * 
                               _settings.JitterFactor * delay;
                    await Task.Delay((int)(delay + jitter), cancellationToken);
                    delay = Math.Min(
                        (int)(delay * _settings.BackoffMultiplier),
                        _settings.MaxRetryDelayMs);
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                _circuitBreaker.RecordFailure();
                throw;
            }
        }

        throw new AnthropicApiException(
            $"Operation failed after {attempts} attempts. Last error: {lastException?.Message}",
            lastException);
    }

    public async Task<bool> ValidateApiConnection()
    {
        try
        {
            return await ExecuteWithRetryAsync(async (cancellationToken) =>
            {
                var requestBody = new AnthropicRequest
                {
                    Model = _settings.DefaultModel,
                    MaxTokens = 1,
                    Messages = [
                        new AnthropicMessage
                        {
                            Role = "user",
                            Content = [new AnthropicMessageContent
                            {
                                Type = "text",
                                Text = "Hello"
                            }]
                        }
                    ],
                    Temperature = 0
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "/v1/messages",
                    requestBody,
                    cancellationToken);
                
                return response.IsSuccessStatusCode;
            }, priority: 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"API validation failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public class AnthropicApiException : Exception
{
    public AnthropicApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message)
        : base(message)
    {
    }
}

public class AnthropicRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; set; }

    [JsonPropertyName("messages")]
    public required List<AnthropicMessage> Messages { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required List<AnthropicMessageContent> Content { get; set; }
}

public class AnthropicMessageContent
{
    [JsonPropertyName("type")]
    public required string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

public class AnthropicResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; } = "assistant";

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; set; }

    [JsonPropertyName("content")]
    public required List<AnthropicMessageContent> Content { get; set; }
}