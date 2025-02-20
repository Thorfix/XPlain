using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace XPlain.Services
{
    public class RateLimitingService : IRateLimitingService
    {
        private readonly ConcurrentDictionary<string, ProviderRateLimiter> _limiters = new();
        private readonly IOptions<RateLimitSettings> _settings;

        public RateLimitingService(IOptions<RateLimitSettings> settings)
        {
            _settings = settings;
        }

        public async Task AcquirePermitAsync(string provider, int priority = 0, CancellationToken cancellationToken = default)
        {
            var limiter = _limiters.GetOrAdd(provider, key => new ProviderRateLimiter(_settings.Value));
            await limiter.AcquireAsync(priority, cancellationToken);
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

            public ProviderRateLimiter(RateLimitSettings settings)
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
}