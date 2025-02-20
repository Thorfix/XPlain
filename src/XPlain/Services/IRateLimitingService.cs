using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IRateLimitingService
    {
        /// <summary>
        /// Acquire a permit to make an API request
        /// </summary>
        /// <param name="provider">The LLM provider name</param>
        /// <param name="priority">Request priority (higher number = higher priority)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task that completes when the request can proceed</returns>
        Task AcquirePermitAsync(string provider, int priority = 0, CancellationToken cancellationToken = default);

        /// <summary>
        /// Release a permit after an API request completes
        /// </summary>
        /// <param name="provider">The LLM provider name</param>
        void ReleasePermit(string provider);

        /// <summary>
        /// Get current usage metrics for a provider
        /// </summary>
        /// <param name="provider">The LLM provider name</param>
        /// <returns>Current usage statistics</returns>
        RateLimitMetrics GetMetrics(string provider);
    }

    public record RateLimitMetrics
    {
        public int QueuedRequests { get; init; }
        public int ActiveRequests { get; init; }
        public int TotalRequestsProcessed { get; init; }
        public int RateLimitErrors { get; init; }
        public DateTime WindowStartTime { get; init; }
        public int RequestsInCurrentWindow { get; init; }
        public decimal DailyCostIncurred { get; init; }
        public decimal DailyCostLimit { get; init; }
        public int RetryAttempts { get; init; }
    }
}