using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Net;
using System.Security.Authentication;

namespace XPlain.Services;

public class CircuitBreaker
{
    private readonly object _lock = new();
    private readonly double _failureThreshold;
    private readonly int _resetTimeoutMs;
    private CircuitState _state = CircuitState.Closed;
    private DateTime _lastStateChange = DateTime.UtcNow;
    private int _totalRequests;
    private int _failedRequests;
    private readonly Queue<DateTime> _recentFailures = new();

    public CircuitBreaker(double failureThreshold, int resetTimeoutMs)
    {
        _failureThreshold = failureThreshold;
        _resetTimeoutMs = resetTimeoutMs;
    }

    public bool CanProcess()
    {
        lock (_lock)
        {
            CleanupOldFailures();
            
            switch (_state)
            {
                case CircuitState.Closed:
                    return true;
                case CircuitState.Open:
                    if ((DateTime.UtcNow - _lastStateChange).TotalMilliseconds >= _resetTimeoutMs)
                    {
                        _state = CircuitState.HalfOpen;
                        _lastStateChange = DateTime.UtcNow;
                        return true;
                    }
                    return false;
                case CircuitState.HalfOpen:
                    return true;
                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _lastStateChange = DateTime.UtcNow;
                _totalRequests = 0;
                _failedRequests = 0;
                _recentFailures.Clear();
            }
            _totalRequests++;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _totalRequests++;
            _failedRequests++;
            _recentFailures.Enqueue(DateTime.UtcNow);

            if (_state == CircuitState.HalfOpen || 
                (_state == CircuitState.Closed && (double)_failedRequests / _totalRequests >= _failureThreshold))
            {
                _state = CircuitState.Open;
                _lastStateChange = DateTime.UtcNow;
            }
        }
    }

    private void CleanupOldFailures()
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-_resetTimeoutMs);
        while (_recentFailures.Count > 0 && _recentFailures.Peek() < cutoff)
        {
            _recentFailures.Dequeue();
            if (_failedRequests > 0) _failedRequests--;
            if (_totalRequests > 0) _totalRequests--;
        }
    }

    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
}

public class AnthropicClient : IAnthropicClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicSettings _settings;
    private readonly SemaphoreSlim _rateLimiter;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly Random _random = new();
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestInterval = 1000; // 1 second between requests for rate limiting

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests, // 429
        HttpStatusCode.ServiceUnavailable, // 503
        HttpStatusCode.GatewayTimeout, // 504
        HttpStatusCode.BadGateway, // 502
        HttpStatusCode.InternalServerError, // 500
        HttpStatusCode.RequestTimeout // 408
    };

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
        _circuitBreaker = new CircuitBreaker(
            _settings.CircuitBreakerFailureThreshold,
            _settings.CircuitBreakerResetTimeoutMs);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
    {
        if (!_circuitBreaker.CanProcess())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open. Too many recent failures.");
        }

        var attempts = 0;
        var delay = _settings.InitialRetryDelayMs;

        while (true)
        {
            try
            {
                attempts++;
                var result = await operation();
                _circuitBreaker.RecordSuccess();
                return result;
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode.HasValue && 
                RetryableStatusCodes.Contains(ex.StatusCode.Value) && 
                attempts < _settings.MaxRetryAttempts)
            {
                _circuitBreaker.RecordFailure();

                // Calculate delay with jitter
                var jitter = _random.NextDouble() * _settings.JitterFactor * delay;
                var actualDelay = delay + (int)jitter;
                
                await Task.Delay(actualDelay);
                
                // Exponential backoff
                delay = (int)(delay * _settings.BackoffMultiplier);
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure();
                throw new AnthropicApiException("Failed to communicate with Anthropic API", ex);
            }
        }
    }

    public async Task<string> AskQuestion(string question, string codeContext)
    {
        await _rateLimiter.WaitAsync();
        try
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest.TotalMilliseconds < MinRequestInterval)
                {
                    await Task.Delay(MinRequestInterval - (int)timeSinceLastRequest.TotalMilliseconds);
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
            });
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
            return await ExecuteWithRetryAsync(async () =>
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
            });
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