using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using XPlain.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels; // For older .NET versions that don't have PriorityQueue

namespace XPlain.Services
{
    public class ProviderRateLimiter
    {
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly object _syncLock = new();
        private readonly RateLimitSettings _settings;
        private DateTime _windowStart;
        private int _requestsInWindow;
        private int _totalRequests;
        private int _rateLimitErrors;
        private readonly Queue<(TaskCompletionSource<bool>, int)> _queue = new();
        private readonly ILogger<RateLimitingService> _logger;

        public ProviderRateLimiter(RateLimitSettings settings, ILogger<RateLimitingService> logger)
        {
            _settings = settings;
            _logger = logger;
            _concurrencyLimiter = new SemaphoreSlim(settings.MaxConcurrentRequests);
            _windowStart = DateTime.UtcNow;
        }

        public async Task AcquireAsync(int priority, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            lock (_syncLock)
            {
                _queue.Enqueue((tcs, priority));
            }

            await ProcessQueueAsync();
            await tcs.Task;
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            
            lock (_syncLock)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStart > TimeSpan.FromSeconds(_settings.WindowSeconds))
                {
                    _windowStart = now;
                    _requestsInWindow = 0;
                }

                if (_requestsInWindow >= _settings.RequestsPerWindow)
                {
                    _rateLimitErrors++;
                    throw new RateLimitExceededException($"Rate limit exceeded: {_settings.RequestsPerWindow} requests per {_settings.WindowSeconds} seconds");
                }

                _requestsInWindow++;
                _totalRequests++;
            }
        }

        private async Task ProcessQueueAsync()
        {
            (TaskCompletionSource<bool> nextTcs, int priority) = (null, 0);
            
            lock (_syncLock)
            {
                if (_queue.Count > 0)
                {
                    nextTcs = _queue.Dequeue().Item1;
                }
            }
            
            if (nextTcs != null)
            {
                nextTcs.TrySetResult(true);
            }
        }

        public void Release()
        {
            _concurrencyLimiter.Release();
        }

        public RateLimitMetrics GetMetrics()
        {
            lock (_syncLock)
            {
                return new RateLimitMetrics
                {
                    QueuedRequests = _queue.Count,
                    ActiveRequests = _settings.MaxConcurrentRequests - _concurrencyLimiter.CurrentCount,
                    TotalRequestsProcessed = _totalRequests,
                    RateLimitErrors = _rateLimitErrors,
                    WindowStartTime = _windowStart,
                    RequestsInCurrentWindow = _requestsInWindow
                };
            }
        }
    }
    
    public class RateLimitingService : IRateLimitingService
    {
        private readonly ConcurrentDictionary<string, ProviderRateLimiter> _limiters = new();
        private readonly IOptions<RateLimitSettings> _settings;
        private readonly ILogger<RateLimitingService> _logger;
        private readonly ConcurrentDictionary<string, CostTracker> _costTrackers = new();

        public RateLimitingService(
            IOptions<RateLimitSettings> settings,
            ILogger<RateLimitingService> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task AcquirePermitAsync(string provider, int priority = 0, CancellationToken cancellationToken = default)
        {
            var settings = GetProviderSettings(provider);
            var limiter = _limiters.GetOrAdd(provider, key => new ProviderRateLimiter(settings, _logger));
            var costTracker = _costTrackers.GetOrAdd(provider, key => new CostTracker(settings));

            if (costTracker.WouldExceedDailyLimit())
            {
                _logger.LogError($"Daily cost limit would be exceeded for provider {provider}");
                throw new CostLimitExceededException($"Daily cost limit of ${settings.DailyCostLimit} would be exceeded for provider {provider}");
            }

            int retryCount = 0;
            int delayMs = settings.InitialRetryDelayMs;

            while (true)
            {
                try
                {
                    await limiter.AcquireAsync(priority, cancellationToken);
                    costTracker.TrackRequest();
                    return;
                }
                catch (RateLimitExceededException) when (retryCount < settings.DefaultRetryCount)
                {
                    _logger.LogWarning($"Rate limit exceeded for provider {provider}, attempt {retryCount + 1} of {settings.DefaultRetryCount}");
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs = (int)Math.Min(delayMs * settings.RetryBackoffMultiplier, settings.MaxRetryDelayMs);
                    retryCount++;
                }
            }
        }

        private RateLimitSettings GetProviderSettings(string provider)
        {
            var baseSettings = _settings.Value;
            if (!baseSettings.ProviderSpecificSettings.TryGetValue(provider, out var providerSettings))
            {
                return baseSettings;
            }

            return new RateLimitSettings
            {
                RequestsPerWindow = providerSettings.RequestsPerWindow ?? baseSettings.RequestsPerWindow,
                WindowSeconds = providerSettings.WindowSeconds ?? baseSettings.WindowSeconds,
                MaxConcurrentRequests = providerSettings.MaxConcurrentRequests ?? baseSettings.MaxConcurrentRequests,
                DefaultRetryCount = baseSettings.DefaultRetryCount,
                InitialRetryDelayMs = baseSettings.InitialRetryDelayMs,
                MaxRetryDelayMs = baseSettings.MaxRetryDelayMs,
                RetryBackoffMultiplier = baseSettings.RetryBackoffMultiplier,
                CostPerRequest = providerSettings.CostPerRequest ?? baseSettings.CostPerRequest,
                DailyCostLimit = providerSettings.DailyCostLimit ?? baseSettings.DailyCostLimit
            };
        }

        public void ReleasePermit(string provider)
        {
            if (_limiters.TryGetValue(provider, out var limiter))
            {
                limiter.Release();
            }
        }

        public RateLimitMetrics GetMetrics(string provider)
        {
            if (_limiters.TryGetValue(provider, out var limiter))
            {
                return limiter.GetMetrics();
            }
            return new RateLimitMetrics();
        }
        
        public async Task<bool> WaitForAvailabilityAsync(string provider, CancellationToken cancellationToken = default)
        {
            try
            {
                var settings = GetProviderSettings(provider);
                var limiter = _limiters.GetOrAdd(provider, key => new ProviderRateLimiter(settings, _logger));
                
                // Check if we're currently at or near rate limit
                var currentMetrics = limiter.GetMetrics();
                
                if (currentMetrics.RequestsInCurrentWindow >= settings.RequestsPerWindow * 0.9)
                {
                    // We're close to the rate limit, wait for next window
                    var timeToNextWindow = TimeSpan.FromSeconds(settings.WindowSeconds) - 
                        (DateTime.UtcNow - currentMetrics.WindowStartTime);
                    
                    if (timeToNextWindow > TimeSpan.Zero && timeToNextWindow < TimeSpan.FromSeconds(settings.WindowSeconds))
                    {
                        try
                        {
                            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                                cancellationToken, 
                                timeoutCts.Token);
                                
                            await Task.Delay(timeToNextWindow, linkedCts.Token);
                        }
                        catch (TaskCanceledException)
                        {
                            return false;
                        }
                    }
                }
                
                // Don't actually acquire the permit here, just check if we would be able to
                var costTracker = _costTrackers.GetOrAdd(provider, key => new CostTracker(settings));
                if (costTracker.WouldExceedDailyLimit())
                {
                    _logger.LogWarning($"Daily cost limit would be exceeded for provider {provider}");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not check availability for provider {provider}");
                return false;
            }
        }

        public bool CanMakeRequest(string provider)
        {
            return WaitForAvailabilityAsync(provider).GetAwaiter().GetResult();
        }

        private class PriorityQueue<TValue, TPriority>
        {
            private readonly List<(TValue Value, TPriority Priority)> _items = new();
            
            public int Count => _items.Count;
            
            public void Enqueue(TValue value, TPriority priority)
            {
                _items.Add((value, priority));
                _items.Sort((a, b) => Comparer<TPriority>.Default.Compare(a.Priority, b.Priority));
            }
            
            public bool TryPeek(out TValue value, out TPriority priority)
            {
                if (_items.Count == 0)
                {
                    value = default;
                    priority = default;
                    return false;
                }
                
                value = _items[0].Value;
                priority = _items[0].Priority;
                return true;
            }
            
            public TValue Dequeue()
            {
                if (_items.Count == 0)
                {
                    throw new InvalidOperationException("Queue is empty");
                }
                
                var result = _items[0].Value;
                _items.RemoveAt(0);
                return result;
            }
        }

        private class ProviderRateLimiter
        {
            private readonly SemaphoreSlim _concurrencyLimiter;
            private readonly PriorityQueue<TaskCompletionSource<bool>, int> _queue;
            private readonly RateLimitSettings _settings;
            private readonly object _syncLock = new();
            private DateTime _windowStart;
            private int _requestsInWindow;
            private int _totalRequests;
            private int _rateLimitErrors;

            public ProviderRateLimiter(RateLimitSettings settings, ILogger<RateLimitingService> logger)
            {
                _settings = settings;
                _concurrencyLimiter = new SemaphoreSlim(settings.MaxConcurrentRequests);
                _queue = new PriorityQueue<TaskCompletionSource<bool>, int>();
                _windowStart = DateTime.UtcNow;
            }

            public async Task AcquireAsync(int priority, CancellationToken cancellationToken)
            {
                var tcs = new TaskCompletionSource<bool>();
                lock (_syncLock)
                {
                    _queue.Enqueue(tcs, -priority); // Negative priority so higher numbers have higher priority
                }

                try
                {
                    await ProcessQueueAsync(cancellationToken);
                    await tcs.Task;

                    await _concurrencyLimiter.WaitAsync(cancellationToken);
                    
                    lock (_syncLock)
                    {
                        var now = DateTime.UtcNow;
                        if (now - _windowStart > TimeSpan.FromSeconds(_settings.WindowSeconds))
                        {
                            _windowStart = now;
                            _requestsInWindow = 0;
                        }

                        if (_requestsInWindow >= _settings.RequestsPerWindow)
                        {
                            _rateLimitErrors++;
                            throw new RateLimitExceededException($"Rate limit exceeded: {_settings.RequestsPerWindow} requests per {_settings.WindowSeconds} seconds");
                        }

                        _requestsInWindow++;
                        _totalRequests++;
                    }
                }
                catch (Exception)
                {
                    _concurrencyLimiter.Release();
                    throw;
                }
            }

            private async Task ProcessQueueAsync(CancellationToken cancellationToken)
            {
                while (true)
                {
                    TaskCompletionSource<bool> nextTcs;
                    lock (_syncLock)
                    {
                        if (!_queue.TryPeek(out nextTcs, out _))
                        {
                            return;
                        }
                        if (nextTcs.Task.IsCompleted)
                        {
                            _queue.Dequeue();
                            continue;
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        nextTcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    nextTcs.TrySetResult(true);
                    return;
                }
            }

            public void Release()
            {
                _concurrencyLimiter.Release();
            }

            public RateLimitMetrics GetMetrics()
            {
                lock (_syncLock)
                {
                    return new RateLimitMetrics
                    {
                        QueuedRequests = _queue.Count,
                        ActiveRequests = _settings.MaxConcurrentRequests - _concurrencyLimiter.CurrentCount,
                        TotalRequestsProcessed = _totalRequests,
                        RateLimitErrors = _rateLimitErrors,
                        WindowStartTime = _windowStart,
                        RequestsInCurrentWindow = _requestsInWindow
                    };
                }
            }
        }
    }

    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message) { }
    }

    public class CostLimitExceededException : Exception
    {
        public CostLimitExceededException(string message) : base(message) { }
    }

    internal class CostTracker
    {
        private readonly RateLimitSettings _settings;
        private decimal _dailyCost;
        private DateTime _lastReset = DateTime.UtcNow;
        private readonly object _lock = new();

        public CostTracker(RateLimitSettings settings)
        {
            _settings = settings;
        }

        public bool WouldExceedDailyLimit()
        {
            lock (_lock)
            {
                CheckAndResetDaily();
                return _dailyCost + _settings.CostPerRequest > _settings.DailyCostLimit;
            }
        }

        public void TrackRequest()
        {
            lock (_lock)
            {
                CheckAndResetDaily();
                _dailyCost += _settings.CostPerRequest;
            }
        }

        private void CheckAndResetDaily()
        {
            var now = DateTime.UtcNow;
            if (now.Date > _lastReset.Date)
            {
                _dailyCost = 0;
                _lastReset = now;
            }
        }
    }
}