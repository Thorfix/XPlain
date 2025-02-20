using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services.LoadTesting
{
    public class LoadTestEngine : ILoadTestContext
    {
        private readonly ILogger<LoadTestEngine> _logger;
        private readonly ConcurrentDictionary<string, double> _metrics = new();
        private readonly ConcurrentDictionary<string, int> _counters = new();
        private volatile int _activeUsers;
        private readonly object _lock = new();
        private readonly MLModelTrainingService _mlTrainingService;
        private readonly ConcurrentQueue<TrainingDataPoint> _trainingData = new();

        public LoadTestEngine(
            ILogger<LoadTestEngine> logger,
            MLModelTrainingService mlTrainingService)
        {
            _logger = logger;
            _mlTrainingService = mlTrainingService;
        }

        private void CollectTrainingData(string query, bool cacheHit, bool predictionCorrect, double responseTime)
        {
            _trainingData.Enqueue(new TrainingDataPoint
            {
                Query = query,
                Timestamp = DateTime.UtcNow,
                CacheHit = cacheHit,
                PredictionCorrect = predictionCorrect,
                ResponseTime = responseTime,
                LoadLevel = _activeUsers
            });

            // Periodically save training data
            if (_trainingData.Count >= 1000)
            {
                _ = Task.Run(async () =>
                {
                    var dataPoints = new List<TrainingDataPoint>();
                    while (_trainingData.Count > 0 && _trainingData.TryDequeue(out var point))
                    {
                        dataPoints.Add(point);
                    }
                    await _mlTrainingService.AddTrainingDataAsync(dataPoints);
                });
            }
        }

        private class TrainingDataPoint
        {
            public string Query { get; set; }
            public DateTime Timestamp { get; set; }
            public bool CacheHit { get; set; }
            public bool PredictionCorrect { get; set; }
            public double ResponseTime { get; set; }
            public int LoadLevel { get; set; }
        }

        public LoadTestEngine(ILogger<LoadTestEngine> logger)
        {
            _logger = logger;
        }

        public async Task RunScenarioAsync(ILoadTestScenario scenario, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Starting load test scenario: {scenario.Name}");
            var profile = scenario.Profile;
            
            var tasks = new List<Task>();
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < profile.Duration && !cancellationToken.IsCancellationRequested)
            {
                var targetUsers = CalculateTargetUsers(startTime, profile);
                
                while (_activeUsers < targetUsers)
                {
                    tasks.Add(RunUserSessionAsync(scenario, cancellationToken));
                    Interlocked.Increment(ref _activeUsers);
                }
                
                await Task.Delay(1000, cancellationToken);
            }
            
            await Task.WhenAll(tasks);
        }

        private int CalculateTargetUsers(DateTime startTime, LoadTestProfile profile)
        {
            if (profile.RampUpPeriod == TimeSpan.Zero)
                return profile.ConcurrentUsers;

            var elapsed = DateTime.UtcNow - startTime;
            var progress = Math.Min(1.0, elapsed.TotalMilliseconds / profile.RampUpPeriod.TotalMilliseconds);
            return (int)(profile.ConcurrentUsers * progress);
        }

        private async Task RunUserSessionAsync(ILoadTestScenario scenario, CancellationToken cancellationToken)
        {
            try
            {
                await scenario.ExecuteAsync(this, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in user session for scenario {scenario.Name}");
                _metrics.AddOrUpdate("errors", 1, (_, v) => v + 1);
            }
            finally
            {
                Interlocked.Decrement(ref _activeUsers);
            }
        }

        public async Task SimulateUserActionAsync(Func<Task> action)
        {
            var start = DateTime.UtcNow;
            try
            {
                await action();
                var duration = (DateTime.UtcNow - start).TotalMilliseconds;
                UpdateResponseTime(duration);
                _counters.AddOrUpdate("totalRequests", 1, (_, v) => v + 1);
            }
            catch
            {
                _counters.AddOrUpdate("errors", 1, (_, v) => v + 1);
                throw;
            }
        }

        private void UpdateResponseTime(double duration)
        {
            lock (_lock)
            {
                if (!_metrics.TryGetValue("avgResponseTime", out var currentAvg))
                {
                    _metrics["avgResponseTime"] = duration;
                    return;
                }

                var totalRequests = _counters.GetOrAdd("totalRequests", 0);
                _metrics["avgResponseTime"] = ((currentAvg * totalRequests) + duration) / (totalRequests + 1);
            }
        }

        public Task<LoadTestMetrics> GetCurrentMetricsAsync()
        {
            var metrics = new LoadTestMetrics
            {
                ActiveUsers = _activeUsers,
                TotalRequests = _counters.GetOrAdd("totalRequests", 0),
                AverageResponseTime = _metrics.GetOrAdd("avgResponseTime", 0),
                ErrorCount = _counters.GetOrAdd("errors", 0),
                CustomMetrics = new Dictionary<string, double>(_metrics)
            };
            
            return Task.FromResult(metrics);
        }

        public Task LogMetricAsync(string name, double value)
        {
            _metrics.AddOrUpdate(name, value, (_, _) => value);
            return Task.CompletedTask;
        }
    }
}