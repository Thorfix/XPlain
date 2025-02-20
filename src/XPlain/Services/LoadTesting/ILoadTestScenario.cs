using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting
{
    public interface ILoadTestScenario
    {
        string Name { get; }
        LoadTestProfile Profile { get; }
        Task ExecuteAsync(ILoadTestContext context, CancellationToken cancellationToken);
    }

    public class LoadTestProfile
    {
        public int ConcurrentUsers { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeSpan RampUpPeriod { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public interface ILoadTestContext
    {
        Task SimulateUserActionAsync(Func<Task> action);
        Task<LoadTestMetrics> GetCurrentMetricsAsync();
        Task LogMetricAsync(string name, double value);
    }

    public class LoadTestMetrics
    {
        public int ActiveUsers { get; set; }
        public int TotalRequests { get; set; }
        public double AverageResponseTime { get; set; }
        public int ErrorCount { get; set; }
        public Dictionary<string, double> CustomMetrics { get; set; } = new();
    }
}