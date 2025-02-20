using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Net;
using System.Security.Authentication;

namespace XPlain.Services;

public class TokenBucket
{
    private readonly object _lock = new();
    private double _tokens;
    private readonly double _capacity;
    private readonly double _refillRate;
    private DateTime _lastRefillTime;

    public TokenBucket(double capacity, double refillRate)
    {
        _capacity = capacity;
        _refillRate = refillRate;
        _tokens = capacity;
        _lastRefillTime = DateTime.UtcNow;
    }

    public async Task<bool> ConsumeTokenAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_lock)
            {
                RefillTokens();
                if (_tokens >= 1)
                {
                    _tokens--;
                    return true;
                }
            }
            await Task.Delay(50, cancellationToken);
        }
        return false;
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var timePassed = (now - _lastRefillTime).TotalSeconds;
        var tokensToAdd = timePassed * _refillRate;
        _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
        _lastRefillTime = now;
    }
}

public class RequestQueue
{
    private class QueueItem
    {
        public required Func<CancellationToken, Task<string>> Operation { get; init; }
        public required TaskCompletionSource<string> TaskCompletion { get; init; }
        public required CancellationToken CancellationToken { get; init; }
        public DateTime EnqueueTime { get; init; } = DateTime.UtcNow;
        public int Priority { get; init; }
    }

    private readonly PriorityQueue<QueueItem, int> _queue = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly TokenBucket _tokenBucket;
    private readonly CancellationTokenSource _processingCts = new();
    private Task _processingTask;
    private readonly int _requestTimeout;

    public RequestQueue(TokenBucket tokenBucket, int requestTimeoutMs)
    {
        _tokenBucket = tokenBucket;
        _requestTimeout = requestTimeoutMs;
        _processingTask = ProcessQueueAsync();
    }

    public async Task<string> EnqueueRequest(Func<CancellationToken, Task<string>> operation, int priority = 0, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<string>();
        var item = new QueueItem
        {
            Operation = operation,
            TaskCompletion = tcs,
            CancellationToken = cancellationToken,
            Priority = priority
        };

        await _queueLock.WaitAsync();
        try
        {
            _queue.Enqueue(item, priority);
        }
        finally
        {
            _queueLock.Release();
        }

        using var timeoutCts = new CancellationTokenSource(_requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        try
        {
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException("Request timed out while waiting in queue");
        }
    }

    private async Task ProcessQueueAsync()
    {
        while (!_processingCts.Token.IsCancellationRequested)
        {
            QueueItem item = null;
            
            await _queueLock.WaitAsync();
            try
            {
                if (_queue.TryDequeue(out item, out _))
                {
                    if ((DateTime.UtcNow - item.EnqueueTime).TotalMilliseconds > _requestTimeout)
                    {
                        item.TaskCompletion.TrySetException(new TimeoutException("Request timed out while waiting in queue"));
                        continue;
                    }
                }
            }
            finally
            {
                _queueLock.Release();
            }

            if (item != null)
            {
                try
                {
                    if (await _tokenBucket.ConsumeTokenAsync(item.CancellationToken))
                    {
                        var result = await item.Operation(item.CancellationToken);
                        item.TaskCompletion.TrySetResult(result);
                    }
                }
                catch (Exception ex)
                {
                    item.TaskCompletion.TrySetException(ex);
                }
            }
            else
            {
                await Task.Delay(50);
            }
        }
    }

    public void Dispose()
    {
        _processingCts.Cancel();
        _processingCts.Dispose();
        _queueLock.Dispose();
    }
}

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

public class AnthropicClient : BaseLLMProvider, IAnthropicClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicSettings _settings;
    public override string ProviderName => "Anthropic";
    public override string ModelName => _settings.DefaultModel;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly Random _random = new();
    private readonly TokenBucket _tokenBucket;
    private readonly RequestQueue _requestQueue;
    private const int RequestTimeoutMs = 30000; // 30 seconds timeout for queued requests
    private const double TokensPerSecond = 1.0; // Base rate of 1 request per second
    private const double BurstCapacity = 5.0; // Allow bursts of up to 5 requests

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests, // 429
        HttpStatusCode.ServiceUnavailable, // 503
        HttpStatusCode.GatewayTimeout, // 504
        HttpStatusCode.BadGateway, // 502
        HttpStatusCode.InternalServerError, // 500
        HttpStatusCode.RequestTimeout // 408
    };

    public AnthropicClient(IOptions<AnthropicSettings> settings, ICacheProvider cacheProvider)
        : base(cacheProvider)
    {
        _settings = settings.Value;
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
            
        _tokenBucket = new TokenBucket(BurstCapacity, TokensPerSecond);
        _requestQueue = new RequestQueue(_tokenBucket, RequestTimeoutMs);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, int priority = 0, CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanProcess())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open. Too many recent failures.");
        }

        async Task<T> RetryOperation(CancellationToken ct)
        {
            var attempts = 0;
            var delay = _settings.InitialRetryDelayMs;

            while (true)
            {
                try
                {
                    attempts++;
                    var result = await operation(ct);
                    _circuitBreaker.RecordSuccess();
                    return result;
                }
                catch (HttpRequestException ex) when (
                    ex.StatusCode.HasValue && 
                    RetryableStatusCodes.Contains(ex.StatusCode.Value) && 
                    attempts < _settings.MaxRetryAttempts)
                {
                    _circuitBreaker.RecordFailure();

                    if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        // Parse rate limit headers if available
                        if (ex.Headers != null)
                        {
                            if (ex.Headers.TryGetValue("x-ratelimit-reset", out var resetValue) && 
                                int.TryParse(resetValue.FirstOrDefault(), out var resetSeconds))
                            {
                                delay = resetSeconds * 1000; // Convert to milliseconds
                            }
                        }
                    }

                    // Calculate delay with jitter
                    var jitter = _random.NextDouble() * _settings.JitterFactor * delay;
                    var actualDelay = delay + (int)jitter;
                    
                    await Task.Delay(actualDelay, ct);
                    
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

        return await _requestQueue.EnqueueRequest(RetryOperation, priority, cancellationToken);
    }

    protected override async Task<string> GetCompletionInternalAsync(string prompt)
    {
        return await ExecuteWithRetryAsync(async (cancellationToken) =>
        {
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

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", requestBody, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Track rate limit information from headers
            if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues) &&
                int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
            {
                // Log or monitor remaining rate limit
                Debug.WriteLine($"Rate limit remaining: {remaining}");
            }

            var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: cancellationToken);
            return result?.Content.FirstOrDefault()?.Text.Trim() ?? "No response received from the API.";
        });
    }

    public async Task<string> AskQuestion(string question, string codeContext)
    {
        return await ExecuteWithRetryAsync(async (cancellationToken) =>
        {
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

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", requestBody, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Track rate limit information from headers
            if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues) &&
                int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
            {
                // Log or monitor remaining rate limit
                Debug.WriteLine($"Rate limit remaining: {remaining}");
            }

            var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: cancellationToken);
            return result?.Content.FirstOrDefault()?.Text.Trim() ?? "No response received from the API.";
        });
    }

    public async Task<bool> ValidateApiConnection()
    {
        try
        {
            return await ExecuteWithRetryAsync(async (cancellationToken) =>
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

                HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", requestBody, cancellationToken);
                Debug.WriteLine(await response.Content.ReadAsStringAsync(cancellationToken));
                return response.IsSuccessStatusCode;
            }, priority: 1); // Higher priority for validation requests
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
        _requestQueue.Dispose();
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