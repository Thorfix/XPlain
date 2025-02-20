using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting.Scenarios
{
    public class CacheOptimizationScenario : ILoadTestScenario
    {
        private readonly IAutomaticCacheOptimizer _cacheOptimizer;
        private readonly ICacheProvider _cacheProvider;
        private readonly QueryDistributionGenerator _queryGenerator;
        private readonly ConcurrentDictionary<string, CacheOptimizationMetrics> _optimizationResults;

        public string Name => "Cache Optimization Test";
        public LoadTestProfile Profile { get; }

        public CacheOptimizationScenario(
            IAutomaticCacheOptimizer cacheOptimizer,
            ICacheProvider cacheProvider,
            QueryDistributionGenerator queryGenerator,
            LoadTestProfile profile)
        {
            _cacheOptimizer = cacheOptimizer;
            _cacheProvider = cacheProvider;
            _queryGenerator = queryGenerator;
            Profile = profile;
            _optimizationResults = new ConcurrentDictionary<string, CacheOptimizationMetrics>();
        }

        public async Task ExecuteAsync(ILoadTestContext context, CancellationToken cancellationToken)
        {
            // Track initial optimization state
            var initialState = await _cacheOptimizer.GetOptimizationMetricsAsync();
            await LogOptimizationState("initial", initialState, context);

            // Run different traffic patterns and monitor optimization response
            foreach (var pattern in new[] { "Steady", "Burst", "Fluctuating" })
            {
                await RunTrafficPattern(pattern, context, cancellationToken);
                
                // Allow optimization to stabilize
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                
                // Record optimization response
                var currentState = await _cacheOptimizer.GetOptimizationMetricsAsync();
                await LogOptimizationState(pattern, currentState, context);
            }
        }

        private async Task RunTrafficPattern(string pattern, ILoadTestContext context, CancellationToken cancellationToken)
        {
            var duration = TimeSpan.FromMinutes(5);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < duration && !cancellationToken.IsCancellationRequested)
            {
                var queries = pattern switch
                {
                    "Steady" => GenerateSteadyTraffic(),
                    "Burst" => GenerateBurstTraffic(),
                    "Fluctuating" => GenerateFluctuatingTraffic(),
                    _ => throw new ArgumentException($"Unknown pattern: {pattern}")
                };

                foreach (var query in queries)
                {
                    await context.SimulateUserActionAsync(async () =>
                    {
                        // Track pre-optimization state
                        var preOptimizationState = await _cacheOptimizer.GetOptimizationMetricsAsync();
                        
                        // Perform cache operation
                        await _cacheProvider.TryGetAsync(query, cancellationToken);
                        
                        // Track post-optimization state
                        var postOptimizationState = await _cacheOptimizer.GetOptimizationMetricsAsync();
                        
                        // Record optimization response
                        await LogOptimizationResponse(pattern, preOptimizationState, postOptimizationState, context);
                    });
                }

                await Task.Delay(_queryGenerator.GetNextQueryDelay(), cancellationToken);
            }
        }

        private string[] GenerateSteadyTraffic()
        {
            return Enumerable.Range(0, 10)
                .Select(_ => _queryGenerator.GenerateQuery())
                .ToArray();
        }

        private string[] GenerateBurstTraffic()
        {
            return Enumerable.Range(0, 50)
                .Select(_ => _queryGenerator.GenerateQuery())
                .ToArray();
        }

        private string[] GenerateFluctuatingTraffic()
        {
            var random = new Random();
            return Enumerable.Range(0, random.Next(5, 30))
                .Select(_ => _queryGenerator.GenerateQuery())
                .ToArray();
        }

        private async Task LogOptimizationState(string phase, OptimizationMetrics metrics, ILoadTestContext context)
        {
            var state = new CacheOptimizationMetrics
            {
                CacheSize = metrics.CurrentCacheSize,
                EvictionRate = metrics.EvictionRate,
                HitRate = metrics.HitRate,
                MemoryUsage = metrics.MemoryUsage,
                OptimizationLatency = metrics.OptimizationLatency
            };

            _optimizationResults.AddOrUpdate(phase, state, (_, _) => state);

            await context.LogMetricAsync($"optimization_{phase}_cache_size", metrics.CurrentCacheSize);
            await context.LogMetricAsync($"optimization_{phase}_eviction_rate", metrics.EvictionRate);
            await context.LogMetricAsync($"optimization_{phase}_hit_rate", metrics.HitRate);
            await context.LogMetricAsync($"optimization_{phase}_memory_usage", metrics.MemoryUsage);
            await context.LogMetricAsync($"optimization_{phase}_latency", metrics.OptimizationLatency);
        }

        private async Task LogOptimizationResponse(
            string pattern,
            OptimizationMetrics before,
            OptimizationMetrics after,
            ILoadTestContext context)
        {
            var optimizationTime = after.LastOptimizationTime - before.LastOptimizationTime;
            var sizeChange = after.CurrentCacheSize - before.CurrentCacheSize;
            var hitRateChange = after.HitRate - before.HitRate;

            await context.LogMetricAsync($"{pattern}_optimization_time", optimizationTime.TotalMilliseconds);
            await context.LogMetricAsync($"{pattern}_size_change", sizeChange);
            await context.LogMetricAsync($"{pattern}_hit_rate_change", hitRateChange);
        }
    }

    internal class CacheOptimizationMetrics
    {
        public int CacheSize { get; set; }
        public double EvictionRate { get; set; }
        public double HitRate { get; set; }
        public double MemoryUsage { get; set; }
        public double OptimizationLatency { get; set; }
    }
}