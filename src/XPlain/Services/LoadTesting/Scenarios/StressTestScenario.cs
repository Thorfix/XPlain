using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting.Scenarios
{
    public class StressTestScenario : ILoadTestScenario
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _mlPredictionService;
        private readonly AutomaticMitigationService _mitigationService;
        private readonly QueryDistributionGenerator _queryGenerator;
        private readonly ConcurrentDictionary<string, PerformanceBoundary> _boundaries;

        public string Name => "System Stress Test";
        public LoadTestProfile Profile { get; }

        public StressTestScenario(
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService,
            AutomaticMitigationService mitigationService,
            QueryDistributionGenerator queryGenerator,
            LoadTestProfile profile)
        {
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
            _mitigationService = mitigationService;
            _queryGenerator = queryGenerator;
            Profile = profile;
            _boundaries = new ConcurrentDictionary<string, PerformanceBoundary>();
        }

        public async Task ExecuteAsync(ILoadTestContext context, CancellationToken cancellationToken)
        {
            // Gradually increase load until system boundaries are found
            var baselineMetrics = await MeasureBaselinePerformance(context, cancellationToken);
            var currentLoad = Profile.ConcurrentUsers;
            var stabilityThreshold = TimeSpan.FromSeconds(30);
            var degradationThreshold = 2.0; // 200% increase in response time

            while (!cancellationToken.IsCancellationRequested)
            {
                var metrics = await RunLoadPhase(context, currentLoad, cancellationToken);
                
                // Check for system boundaries
                if (HasReachedBoundary(metrics, baselineMetrics, degradationThreshold))
                {
                    await LogPerformanceBoundary(context, currentLoad, metrics);
                    // Reduce load and stabilize
                    currentLoad = (int)(currentLoad * 0.7);
                    await Task.Delay(stabilityThreshold, cancellationToken);
                }
                else
                {
                    // Increase load
                    currentLoad = (int)(currentLoad * 1.2);
                }

                // Allow system to stabilize between phases
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        private async Task<SystemMetrics> MeasureBaselinePerformance(ILoadTestContext context, CancellationToken cancellationToken)
        {
            var metrics = new SystemMetrics();
            var samplingPeriod = TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < samplingPeriod && !cancellationToken.IsCancellationRequested)
            {
                var query = _queryGenerator.GenerateQuery();
                await context.SimulateUserActionAsync(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    
                    var cacheResult = await _cacheProvider.TryGetAsync(query, cancellationToken);
                    var predictionResult = await _mlPredictionService.PredictCacheHitAsync(query);
                    
                    sw.Stop();

                    metrics.AddSample(new MetricSample
                    {
                        ResponseTime = sw.ElapsedMilliseconds,
                        CacheHit = cacheResult != null,
                        PredictionAccurate = (cacheResult != null) == predictionResult
                    });
                });
            }

            return metrics;
        }

        private async Task<SystemMetrics> RunLoadPhase(ILoadTestContext context, int concurrentUsers, CancellationToken cancellationToken)
        {
            var metrics = new SystemMetrics();
            var tasks = new List<Task>();

            for (int i = 0; i < concurrentUsers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var query = _queryGenerator.GenerateQuery();
                    await context.SimulateUserActionAsync(async () =>
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        
                        var cacheResult = await _cacheProvider.TryGetAsync(query, cancellationToken);
                        var predictionResult = await _mlPredictionService.PredictCacheHitAsync(query);
                        var mitigationState = await _mitigationService.GetCurrentStateAsync();
                        
                        sw.Stop();

                        metrics.AddSample(new MetricSample
                        {
                            ResponseTime = sw.ElapsedMilliseconds,
                            CacheHit = cacheResult != null,
                            PredictionAccurate = (cacheResult != null) == predictionResult,
                            MitigationActive = mitigationState.MitigationActive
                        });
                    });
                }));
            }

            await Task.WhenAll(tasks);
            return metrics;
        }

        private bool HasReachedBoundary(SystemMetrics current, SystemMetrics baseline, double threshold)
        {
            return current.AverageResponseTime > baseline.AverageResponseTime * threshold ||
                   current.ErrorRate > 0.1 || // 10% error rate
                   current.CacheHitRate < baseline.CacheHitRate * 0.5; // 50% cache hit rate degradation
        }

        private async Task LogPerformanceBoundary(ILoadTestContext context, int load, SystemMetrics metrics)
        {
            var boundary = new PerformanceBoundary
            {
                MaxConcurrentUsers = load,
                AverageResponseTime = metrics.AverageResponseTime,
                ErrorRate = metrics.ErrorRate,
                CacheHitRate = metrics.CacheHitRate,
                PredictionAccuracy = metrics.PredictionAccuracy,
                Timestamp = DateTime.UtcNow
            };

            _boundaries.AddOrUpdate("latest", boundary, (_, _) => boundary);
            
            await context.LogMetricAsync("stress_test_max_users", load);
            await context.LogMetricAsync("stress_test_response_time", metrics.AverageResponseTime);
            await context.LogMetricAsync("stress_test_error_rate", metrics.ErrorRate);
            await context.LogMetricAsync("stress_test_cache_hit_rate", metrics.CacheHitRate);
        }
    }

    public class SystemMetrics
    {
        private readonly ConcurrentBag<MetricSample> _samples = new();

        public void AddSample(MetricSample sample) => _samples.Add(sample);

        public double AverageResponseTime => _samples.Average(s => s.ResponseTime);
        public double ErrorRate => _samples.Count > 0 ? _samples.Count(s => s.ResponseTime > 5000) / (double)_samples.Count : 0;
        public double CacheHitRate => _samples.Count > 0 ? _samples.Count(s => s.CacheHit) / (double)_samples.Count : 0;
        public double PredictionAccuracy => _samples.Count > 0 ? _samples.Count(s => s.PredictionAccurate) / (double)_samples.Count : 0;
    }

    public class MetricSample
    {
        public long ResponseTime { get; set; }
        public bool CacheHit { get; set; }
        public bool PredictionAccurate { get; set; }
        public bool MitigationActive { get; set; }
    }

    public class PerformanceBoundary
    {
        public int MaxConcurrentUsers { get; set; }
        public double AverageResponseTime { get; set; }
        public double ErrorRate { get; set; }
        public double CacheHitRate { get; set; }
        public double PredictionAccuracy { get; set; }
        public DateTime Timestamp { get; set; }
    }
}