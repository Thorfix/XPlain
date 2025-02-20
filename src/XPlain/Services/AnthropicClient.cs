using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Net;
using System.Security.Authentication;

namespace XPlain.Services;

public class DualTokenBucket
{
    private readonly object _lock = new();
    private double _secondTokens;
    private double _minuteTokens;
    private readonly double _secondCapacity;
    private readonly double _minuteCapacity;
    private double _secondRefillRate;
    private double _minuteRefillRate;
    private DateTime _lastRefillTime;
    private int _remainingPerSecond;
    private int _remainingPerMinute;
    private DateTime _lastRateLimitUpdate = DateTime.MinValue;
    private const double RateAdjustmentThreshold = 0.2; // Adjust rates when remaining capacity is below 20%
    private const double RateDecreaseMultiplier = 0.8; // Decrease rates by 20% when approaching limits
    private const double RateIncreaseMultiplier = 1.1; // Increase rates by 10% when limits are healthy
    private readonly object _rateLimitLock = new();

    public record RateLimitInfo(int RemainingPerSecond, int RemainingPerMinute, DateTime ResetTime);

    public DualTokenBucket(double secondCapacity, double minuteCapacity, double secondRefillRate, double minuteRefillRate)
    {
        _secondCapacity = secondCapacity;
        _minuteCapacity = minuteCapacity;
        _secondRefillRate = secondRefillRate;
        _minuteRefillRate = minuteRefillRate;
        _secondTokens = secondCapacity;
        _minuteTokens = minuteCapacity;
        _lastRefillTime = DateTime.UtcNow;
    }

    public async Task<bool> ConsumeTokenAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_lock)
            {
                RefillTokens();
                if (_secondTokens >= 1 && _minuteTokens >= 1)
                {
                    _secondTokens--;
                    _minuteTokens--;
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
        
        lock (_rateLimitLock)
        {
            // Adjust rates based on remaining capacity
            AdjustRatesBasedOnUsage();
            
            // Refill per-second tokens
            var secondTokensToAdd = timePassed * _secondRefillRate;
            _secondTokens = Math.Min(_secondCapacity, _secondTokens + secondTokensToAdd);
            
            // Refill per-minute tokens
            var minuteTokensToAdd = timePassed * _minuteRefillRate / 60.0; // Convert to per-second rate
            _minuteTokens = Math.Min(_minuteCapacity, _minuteTokens + minuteTokensToAdd);
        }
        
        _lastRefillTime = now;
    }

    public void UpdateRateLimits(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("x-ratelimit-remaining-requests", out var requestsRemaining) ||
            !headers.TryGetValues("x-ratelimit-remaining-tokens", out var tokensRemaining) ||
            !headers.TryGetValues("x-ratelimit-reset", out var resetTime))
        {
            return;
        }

        lock (_rateLimitLock)
        {
            if (int.TryParse(requestsRemaining.FirstOrDefault(), out var remainingRequests) &&
                int.TryParse(tokensRemaining.FirstOrDefault(), out var remainingTokens) &&
                DateTimeOffset.TryParse(resetTime.FirstOrDefault(), out var reset))
            {
                _remainingPerSecond = remainingRequests;
                _remainingPerMinute = remainingTokens;
                _lastRateLimitUpdate = DateTime.UtcNow;
                
                // Log rate limit information
                Debug.WriteLine($"Rate limits updated - Requests: {remainingRequests}, Tokens: {remainingTokens}, Reset: {reset}");
            }
        }
    }

    private void AdjustRatesBasedOnUsage()
    {
        if (DateTime.UtcNow - _lastRateLimitUpdate > TimeSpan.FromMinutes(1))
        {
            return; // Don't adjust rates if we haven't received recent rate limit information
        }

        var secondRemainingPercentage = _remainingPerSecond / (double)_secondCapacity;
        var minuteRemainingPercentage = _remainingPerMinute / (double)_minuteCapacity;

        if (secondRemainingPercentage < RateAdjustmentThreshold || minuteRemainingPercentage < RateAdjustmentThreshold)
        {
            // Decrease rates when approaching limits
            _secondRefillRate *= RateDecreaseMultiplier;
            _minuteRefillRate *= RateDecreaseMultiplier;
            Debug.WriteLine($"Decreasing rates - Second: {_secondRefillRate:F2}, Minute: {_minuteRefillRate:F2}");
        }
        else if (secondRemainingPercentage > 0.5 && minuteRemainingPercentage > 0.5)
        {
            // Gradually increase rates when usage is healthy
            _secondRefillRate *= RateIncreaseMultiplier;
            _minuteRefillRate *= RateIncreaseMultiplier;
            
            // Cap at original rates
            _secondRefillRate = Math.Min(_secondRefillRate, TokensPerSecond);
            _minuteRefillRate = Math.Min(_minuteRefillRate, TokensPerMinute);
            
            Debug.WriteLine($"Increasing rates - Second: {_secondRefillRate:F2}, Minute: {_minuteRefillRate:F2}");
        }
    }

    public RateLimitInfo GetCurrentRateLimits()
    {
        lock (_rateLimitLock)
        {
            return new RateLimitInfo(_remainingPerSecond, _remainingPerMinute, _lastRateLimitUpdate);
        }
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

public class RequestBatcher
{
    private class BatchKey : IEquatable<BatchKey>
    {
        public string Model { get; }
        public string Content { get; }
        public int MaxTokens { get; }

        public BatchKey(string model, string content, int maxTokens)
        {
            Model = model;
            Content = content;
            MaxTokens = maxTokens;
        }

        public bool Equals(BatchKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Model == other.Model && Content == other.Content && MaxTokens == other.MaxTokens;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BatchKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Model, Content, MaxTokens);
        }
    }

    private class PendingRequest
    {
        public TaskCompletionSource<string> TaskCompletion { get; }
        public DateTime Timestamp { get; }
        public CancellationToken CancellationToken { get; }

        public PendingRequest(TaskCompletionSource<string> taskCompletion, CancellationToken cancellationToken)
        {
            TaskCompletion = taskCompletion;
            Timestamp = DateTime.UtcNow;
            CancellationToken = cancellationToken;
        }
    }

    private readonly Dictionary<BatchKey, List<PendingRequest>> _pendingRequests = new();
    private readonly object _lock = new();
    private readonly Timer _batchTimer;
    private readonly int _maxBatchAge;
    private readonly Func<BatchKey, List<PendingRequest>, CancellationToken, Task> _batchProcessor;

    public RequestBatcher(int maxBatchAgeMs, Func<BatchKey, List<PendingRequest>, CancellationToken, Task> batchProcessor)
    {
        _maxBatchAge = maxBatchAgeMs;
        _batchProcessor = batchProcessor;
        _batchTimer = new Timer(ProcessBatches, null, 1000, 1000);
    }

    public Task<string> EnqueueRequest(string model, string content, int maxTokens, CancellationToken cancellationToken)
    {
        var key = new BatchKey(model, content, maxTokens);
        var tcs = new TaskCompletionSource<string>();
        var request = new PendingRequest(tcs, cancellationToken);

        lock (_lock)
        {
            if (!_pendingRequests.TryGetValue(key, out var requests))
            {
                requests = new List<PendingRequest>();
                _pendingRequests[key] = requests;
            }
            requests.Add(request);
        }

        return tcs.Task;
    }

    private async void ProcessBatches(object state)
    {
        List<(BatchKey Key, List<PendingRequest> Requests)> batchesToProcess = new();

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _pendingRequests)
            {
                var oldestRequest = kvp.Value.First().Timestamp;
                if ((now - oldestRequest).TotalMilliseconds >= _maxBatchAge)
                {
                    batchesToProcess.Add((kvp.Key, kvp.Value));
                    _pendingRequests.Remove(kvp.Key);
                }
            }
        }

        foreach (var batch in batchesToProcess)
        {
            try
            {
                await _batchProcessor(batch.Key, batch.Requests, CancellationToken.None);
            }
            catch (Exception ex)
            {
                foreach (var request in batch.Requests)
                {
                    request.TaskCompletion.TrySetException(ex);
                }
            }
        }
    }

    public void Dispose()
    {
        _batchTimer.Dispose();
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
        public string RequestKey { get; init; }
        public int StarvationCounter { get; set; } = 0;
        public double Similarity { get; init; } // Similarity score with other requests
    }

    private readonly Dictionary<string, WeakReference<QueueItem>> _activeRequests = new();
    private readonly PriorityQueue<QueueItem, double> _queue = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly TokenBucket _tokenBucket;
    private readonly int _requestTimeout;
    private readonly CancellationTokenSource _processingCts = new();
    private Task _processingTask;
    private readonly Dictionary<string, DateTime> _lastProcessedRequests = new();
    private const int MaxQueuedRequestsPerType = 10;
    private const double SimilarityThreshold = 0.85;
    private const int MaxQueueSize = 1000;
    private readonly object _coalescenceLock = new();

    // LRU cache for recent requests to help with coalescence
    private readonly LinkedList<(string Key, string Content, DateTime Time)> _recentRequests = new();
    private const int MaxRecentRequests = 100;

    private double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2)) return 0;
        
        // Convert to lowercase for comparison
        text1 = text1.ToLowerInvariant();
        text2 = text2.ToLowerInvariant();

        // Use Levenshtein distance for similarity
        int[,] matrix = new int[text1.Length + 1, text2.Length + 1];

        for (int i = 0; i <= text1.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= text2.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= text1.Length; i++)
        {
            for (int j = 1; j <= text2.Length; j++)
            {
                int cost = (text1[i - 1] == text2[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        int distance = matrix[text1.Length, text2.Length];
        double maxLength = Math.Max(text1.Length, text2.Length);
        return 1.0 - (distance / maxLength);
    }

    private async Task<QueueItem> FindOrCreateCoalescedRequest(string content, Func<CancellationToken, Task<string>> operation, int priority, CancellationToken cancellationToken)
    {
        QueueItem existingItem = null;
        double highestSimilarity = 0;

        lock (_coalescenceLock)
        {
            // Clean up expired recent requests
            var cutoff = DateTime.UtcNow.AddSeconds(-30); // Only consider requests from last 30 seconds
            while (_recentRequests.Count > 0 && _recentRequests.First.Value.Time < cutoff)
            {
                _recentRequests.RemoveFirst();
            }

            // Find similar request
            foreach (var (key, recentContent, _) in _recentRequests)
            {
                var similarity = CalculateSimilarity(content, recentContent);
                if (similarity > SimilarityThreshold && similarity > highestSimilarity)
                {
                    if (_activeRequests.TryGetValue(key, out var weakRef) && 
                        weakRef.TryGetTarget(out var item))
                    {
                        existingItem = item;
                        highestSimilarity = similarity;
                    }
                }
            }

            // Add to recent requests
            var requestKey = Guid.NewGuid().ToString();
            _recentRequests.AddLast((requestKey, content, DateTime.UtcNow));
            while (_recentRequests.Count > MaxRecentRequests)
            {
                _recentRequests.RemoveFirst();
            }

            if (existingItem != null)
            {
                Debug.WriteLine($"Coalesced request with similarity {highestSimilarity:F2}");
                return existingItem;
            }

            // Create new request
            var newItem = new QueueItem
            {
                Operation = operation,
                TaskCompletion = new TaskCompletionSource<string>(),
                CancellationToken = cancellationToken,
                Priority = priority,
                RequestKey = requestKey,
                Similarity = 1.0
            };

            _activeRequests[requestKey] = new WeakReference<QueueItem>(newItem);
            return newItem;
        }
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
    private readonly DualTokenBucket _tokenBucket;
    private readonly RequestQueue _requestQueue;
    private readonly RequestBatcher _requestBatcher;
    private const int RequestTimeoutMs = 30000; // 30 seconds timeout for queued requests
    private const double TokensPerSecond = 1.0; // Base rate of 1 request per second
    private const double TokensPerMinute = 50.0; // Base rate of 50 requests per minute
    private const double BurstCapacityPerSecond = 5.0; // Allow bursts of up to 5 requests per second
    private const double BurstCapacityPerMinute = 100.0; // Allow bursts of up to 100 requests per minute
    private const int MaxBatchAgeMs = 500; // Maximum age of a batch before processing

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
            
        _tokenBucket = new DualTokenBucket(
            BurstCapacityPerSecond, 
            BurstCapacityPerMinute,
            TokensPerSecond,
            TokensPerMinute);
            
        _requestBatcher = new RequestBatcher(MaxBatchAgeMs, ProcessBatchAsync);
        _requestQueue = new RequestQueue(_tokenBucket, RequestTimeoutMs);
    }

    private double CalculateEffectivePriority(QueueItem item)
    {
        var waitingTime = (DateTime.UtcNow - item.EnqueueTime).TotalSeconds;
        var starvationBonus = Math.Min(5, item.StarvationCounter) * 2;
        var waitingBonus = Math.Min(10, waitingTime / 30.0) * 3; // Bonus for waiting, max 10 points
        var similarityBonus = (1.0 - item.Similarity) * 2; // Prioritize less similar requests
        
        return -(item.Priority + starvationBonus + waitingBonus + similarityBonus); // Negative for PriorityQueue ordering
    }

    private async Task ProcessQueueAsync()
    {
        while (!_processingCts.Token.IsCancellationRequested)
        {
            try
            {
                await _queueLock.WaitAsync(_processingCts.Token);
                QueueItem item = null;
                
                try
                {
                    // Update priorities and remove cancelled/timeout requests
                    var itemsToRequeue = new List<QueueItem>();
                    while (_queue.Count > 0)
                    {
                        var currentItem = _queue.Dequeue();
                        
                        if (currentItem.CancellationToken.IsCancellationRequested ||
                            (DateTime.UtcNow - currentItem.EnqueueTime).TotalMilliseconds > _requestTimeout)
                        {
                            currentItem.TaskCompletion.TrySetCanceled();
                            continue;
                        }

                        currentItem.StarvationCounter++;
                        var effectivePriority = CalculateEffectivePriority(currentItem);
                        
                        if (item == null)
                        {
                            item = currentItem;
                        }
                        else
                        {
                            itemsToRequeue.Add(currentItem);
                        }
                    }

                    // Requeue items with updated priorities
                    foreach (var requeueItem in itemsToRequeue)
                    {
                        _queue.Enqueue(requeueItem, CalculateEffectivePriority(requeueItem));
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
                        var result = await item.Operation(item.CancellationToken);
                        item.TaskCompletion.TrySetResult(result);
                        
                        lock (_coalescenceLock)
                        {
                            _lastProcessedRequests[item.RequestKey] = DateTime.UtcNow;
                            // Cleanup old processed requests
                            var cutoff = DateTime.UtcNow.AddMinutes(-5);
                            var keysToRemove = _lastProcessedRequests.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
                            foreach (var key in keysToRemove)
                            {
                                _lastProcessedRequests.Remove(key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        item.TaskCompletion.TrySetException(ex);
                    }
                }
                else
                {
                    await Task.Delay(50, _processingCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in queue processing: {ex.Message}");
                await Task.Delay(1000, _processingCts.Token); // Brief delay on error
            }
        }
    }

    private async Task ProcessBatchAsync(BatchKey key, List<PendingRequest> requests, CancellationToken cancellationToken)
    {
        try
        {
            // Calculate batch priority based on age of oldest request
            var oldestRequest = requests.Min(r => r.Timestamp);
            var waitTime = DateTime.UtcNow - oldestRequest;
            var priority = Math.Max(0, 5 - (int)waitTime.TotalSeconds); // Higher priority for older requests

            var response = await ExecuteWithRetryAsync(async (ct) =>
            {
                AnthropicRequest requestBody = new AnthropicRequest
                {
                    Model = key.Model,
                    Messages =
                    [
                        new AnthropicMessage
                        {
                            Role = "user",
                            Content = [new AnthropicMessageContent { Type = "text", Text = key.Content }]
                        }
                    ],
                    MaxTokens = key.MaxTokens,
                    Temperature = 0.7,
                };

                // Get current rate limits before making request
                var rateLimits = _tokenBucket.GetCurrentRateLimits();
                if (rateLimits.RemainingPerSecond < 2 || rateLimits.RemainingPerMinute < 10)
                {
                    // Add adaptive delay when close to limits
                    var delay = Math.Max(
                        1000 / Math.Max(1, rateLimits.RemainingPerSecond),
                        60000 / Math.Max(1, rateLimits.RemainingPerMinute));
                    await Task.Delay((int)delay, ct);
                }

                HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", requestBody, ct);
                
                // Update rate limits from response headers
                _tokenBucket.UpdateRateLimits(response.Headers);
                
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);
                return result?.Content.FirstOrDefault()?.Text.Trim() ?? "No response received from the API.";
            }, priority, cancellationToken);

            // Set the result for all non-cancelled requests in the batch
            foreach (var request in requests.Where(r => !r.CancellationToken.IsCancellationRequested))
            {
                request.TaskCompletion.TrySetResult(response);
            }

            // Log batch processing success
            Debug.WriteLine($"Successfully processed batch of {requests.Count} requests with model {key.Model}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Batch processing failed: {ex.Message}");
            foreach (var request in requests)
            {
                request.TaskCompletion.TrySetException(ex);
            }
        }
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
        var cts = new CancellationTokenSource(RequestTimeoutMs);
        try
        {
            // First try to find similar existing request
            var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
            {
                var requestBody = new AnthropicRequest
                {
                    Model = _settings.DefaultModel,
                    Messages = [new AnthropicMessage
                    {
                        Role = "user",
                        Content = [new AnthropicMessageContent { Type = "text", Text = prompt }]
                    }],
                    MaxTokens = _settings.MaxTokenLimit,
                    Temperature = 0.7,
                };

                return await ProcessRequestWithRateLimiting(requestBody, ct);
            });

            var queueItem = await FindOrCreateCoalescedRequest(prompt, operation, 0, cts.Token);
            
            // Use existing request if found, otherwise queue new request
            if (queueItem.TaskCompletion.Task.IsCompleted)
            {
                Debug.WriteLine("Using existing completed request");
                return await queueItem.TaskCompletion.Task;
            }

            await _queueLock.WaitAsync(cts.Token);
            try
            {
                if (_queue.Count >= MaxQueueSize)
                {
                    throw new InvalidOperationException("Request queue is full");
                }
                _queue.Enqueue(queueItem, CalculateEffectivePriority(queueItem));
            }
            finally
            {
                _queueLock.Release();
            }

            return await queueItem.TaskCompletion.Task;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Request timed out");
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task<string> ProcessRequestWithRateLimiting(AnthropicRequest request, CancellationToken ct)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", request, ct);
        _tokenBucket.UpdateRateLimits(response.Headers);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);
        return result?.Content.FirstOrDefault()?.Text.Trim() ?? "No response received from the API.";
    }

    public async Task<string> AskQuestion(string question, string codeContext)
    {
        var prompt = BuildPrompt(question, codeContext);
        var cts = new CancellationTokenSource(RequestTimeoutMs);
        try
        {
            // Try to batch similar requests
            return await _requestBatcher.EnqueueRequest(
                _settings.DefaultModel,
                prompt,
                _settings.MaxTokenLimit,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Request timed out");
        }
        finally
        {
            cts.Dispose();
        }
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
        try
        {
            // Log final rate limit status
            var finalRateLimits = _tokenBucket.GetCurrentRateLimits();
            Debug.WriteLine($"Final rate limits - Requests/sec: {finalRateLimits.RemainingPerSecond}, Requests/min: {finalRateLimits.RemainingPerMinute}");
            
            _httpClient.Dispose();
            _requestQueue.Dispose();
            _requestBatcher.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during disposal: {ex.Message}");
        }
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