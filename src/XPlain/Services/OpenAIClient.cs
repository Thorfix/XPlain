using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using System.Net;
using System.Diagnostics;
using XPlain.Configuration;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace XPlain.Services;

public class OpenAIClient : BaseLLMProvider, IOpenAIClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OpenAISettings _settings;
    private readonly IRateLimitingService _rateLimitingService;
    private readonly CircuitBreaker _circuitBreaker;
    private const int RequestTimeoutMs = 30000;
    
    public override string ProviderName => "OpenAI";
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

    public OpenAIClient(
        IOptions<OpenAISettings> settings,
        ICacheProvider cacheProvider,
        IRateLimitingService rateLimitingService)
        : base(cacheProvider, rateLimitingService)
    {
        _settings = settings.Value;
        _rateLimitingService = rateLimitingService;

        _httpClient = new HttpClient(
            new StreamingHttpHandler(
                timeout: TimeSpan.FromSeconds(30),
                maxRetries: 3,
                initialRetryDelay: TimeSpan.FromSeconds(1)))
        {
            BaseAddress = new Uri(_settings.ApiEndpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("api-key", _settings.ApiToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _circuitBreaker = new CircuitBreaker(
            _settings.CircuitBreakerFailureThreshold,
            _settings.CircuitBreakerResetTimeoutMs);
    }

    protected override async Task<string> GetCompletionInternalAsync(string prompt)
    {
        var requestBody = new OpenAIRequest
        {
            Model = _settings.DefaultModel,
            Messages = [
                new OpenAIMessage
                {
                    Role = "user",
                    Content = prompt
                }
            ],
            MaxTokens = _settings.MaxTokenLimit,
            Temperature = 0.7
        };

        return await ExecuteWithRetryAsync(async (ct) =>
        {
            var response = await ProcessRequestAsync(requestBody, ct);
            return response?.Choices.FirstOrDefault()?.Message.Content.Trim() ?? 
                   "No response received from the API.";
        }, 0);
    }

    protected override async IAsyncEnumerable<string> GetCompletionStreamInternalAsync(
        string prompt)
    {
        var requestBody = new OpenAIRequest
        {
            Model = _settings.DefaultModel,
            Messages = [
                new OpenAIMessage
                {
                    Role = "user",
                    Content = prompt
                }
            ],
            MaxTokens = _settings.MaxTokenLimit,
            Temperature = 0.7,
            Stream = true
        };

        await foreach (var chunk in ExecuteStreamWithRetryAsync(requestBody))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<string> ExecuteStreamWithRetryAsync(
        OpenAIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
                await foreach (var chunk in ProcessStreamRequestAsync(request, cancellationToken))
                {
                    yield return chunk;
                }
                _circuitBreaker.RecordSuccess();
                yield break;
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode.HasValue &&
                RetryableStatusCodes.Contains(ex.StatusCode.Value))
            {
                lastException = ex;
                _circuitBreaker.RecordFailure();

                if (attempts < _settings.MaxRetryAttempts)
                {
                    var jitter = Random.Shared.NextDouble() * _settings.JitterFactor * delay;
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

        throw new OpenAIApiException(
            $"Stream operation failed after {attempts} attempts. Last error: {lastException?.Message}",
            lastException);
    }

    private async IAsyncEnumerable<string> ProcessStreamRequestAsync(
        OpenAIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Acquire rate limiting permit
        await _rateLimitingService.AcquirePermitAsync(ProviderName, 0, cancellationToken);
        try
        {
            var streamingEndpoint = request.Stream ? "/v1/chat/completions/stream" : "/v1/chat/completions";
            using var response = await _httpClient.PostAsJsonAsync(
                streamingEndpoint,
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data: ")) continue;
                if (line == "data: [DONE]") break;

                var jsonData = line.Substring(6);
                var streamResponse = JsonSerializer.Deserialize<OpenAIStreamResponse>(jsonData);
                
                if (streamResponse?.Choices?.Count > 0 && 
                    !string.IsNullOrEmpty(streamResponse.Choices[0].Delta?.Content))
                {
                    yield return streamResponse.Choices[0].Delta.Content;
                }
            }
        }
        finally
        {
            _rateLimitingService.ReleasePermit(ProviderName);
        }
    }

    private async Task<OpenAIResponse> ProcessRequestAsync(
        OpenAIRequest request,
        CancellationToken cancellationToken)
    {
        // Acquire rate limiting permit
        await _rateLimitingService.AcquirePermitAsync(ProviderName, 0, cancellationToken);
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/v1/chat/completions",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OpenAIResponse>(
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

        throw new OpenAIApiException(
            $"Operation failed after {attempts} attempts. Last error: {lastException?.Message}",
            lastException);
    }

    public async Task<bool> ValidateApiConnection()
    {
        try
        {
            return await ExecuteWithRetryAsync(async (cancellationToken) =>
            {
                var requestBody = new OpenAIRequest
                {
                    Model = _settings.DefaultModel,
                    MaxTokens = 1,
                    Messages = [
                        new OpenAIMessage
                        {
                            Role = "user",
                            Content = "Hello"
                        }
                    ],
                    Temperature = 0
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "/v1/chat/completions",
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

public class OpenAIApiException : Exception
{
    public OpenAIApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class OpenAIRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<OpenAIMessage> Messages { get; set; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class OpenAIMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }
}

public class OpenAIResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("object")]
    public required string Object { get; set; }

    [JsonPropertyName("created")]
    public required int Created { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("usage")]
    public required OpenAIUsage Usage { get; set; }

    [JsonPropertyName("choices")]
    public required List<OpenAIChoice> Choices { get; set; }
}

public class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public required int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public required int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public required int TotalTokens { get; set; }
}

public class OpenAIChoice
{
    [JsonPropertyName("message")]
    public required OpenAIMessage Message { get; set; }

    [JsonPropertyName("delta")]
    public OpenAIMessage? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public required string FinishReason { get; set; }

    [JsonPropertyName("index")]
    public required int Index { get; set; }
}

public class OpenAIStreamResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("object")]
    public required string Object { get; set; }

    [JsonPropertyName("created")]
    public required int Created { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("choices")]
    public required List<OpenAIChoice> Choices { get; set; }
}