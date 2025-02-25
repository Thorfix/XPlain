using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IRateLimitingService
    {
        Task AcquirePermitAsync(string provider, int priority = 0, CancellationToken cancellationToken = default);
        void ReleasePermit(string provider);
        RateLimitMetrics GetMetrics(string provider);
        Task<bool> WaitForAvailabilityAsync(string provider, CancellationToken cancellationToken = default);
        bool CanMakeRequest(string provider) => WaitForAvailabilityAsync(provider).GetAwaiter().GetResult();
    }

    public class RateLimitMetrics
    {
        public int QueuedRequests { get; set; }
        public int ActiveRequests { get; set; }
        public int TotalRequestsProcessed { get; set; }
        public int RateLimitErrors { get; set; }
        public DateTime WindowStartTime { get; set; }
        public int RequestsInCurrentWindow { get; set; }
    }
}