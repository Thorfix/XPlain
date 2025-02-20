using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting
{
    public class StressTester
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _mlPredictionService;
        private readonly AutomaticMitigationService _mitigationService;
        private readonly QueryDistributionGenerator _queryGenerator;
        private readonly ILogger<StressTester> _logger;
        private readonly ConcurrentDictionary<string, PerformanceBoundary> _boundaries;

        public StressTester(
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService,
            AutomaticMitigationService mitigationService,
            QueryDistributionGenerator queryGenerator,
            ILogger<StressTester> logger)
        {
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
            _mitigationService = mitigationService;
            _queryGenerator = queryGenerator;
            _logger = logger;
            _boundaries = new ConcurrentDictionary<string, PerformanceBoundary>();
        }

        public async Task<StressTestReport> RunStressTest(StressTestConfig config, ILoadTestContext context, CancellationToken cancellationToken)
        {
            var report = new StressTestReport();
            
            try
            {
                // Establish baseline performance
                _logger.LogInformation("Establishing baseline performance...");
                var baseline = await MeasureBaselinePerformance(context, cancellationToken);
                report.BaselineMetrics = baseline;

                // Test different aspects of the system
                await Task.WhenAll(
                    TestConcurrentUsers(config, baseline, context, report, cancellationToken),
                    TestCacheCapacity(config, baseline, context, report, cancellationToken),
                    TestMLPredictionLoad(config, baseline, context, report, cancellationToken),
                    TestMitigationEffectiveness(config, baseline, context, report, cancellationToken)
                );

                // Determine system boundaries
                DetermineSystemBoundaries(report);
                
                _logger.LogInformation("Stress test completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stress test");
                report.Errors.Add($"Test failed: {ex.Message}");
            }

            return report;
        }

        private async Task<BaselineMetrics> MeasureBaselinePerformance(ILoadTestContext context, CancellationToken cancellationToken)
        {
            var metrics = new BaselineMetrics();
            var samples = new ConcurrentBag<PerformanceSample>();
            var samplingPeriod = TimeSpan.FromMinutes(1);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < samplingPeriod && !cancellationToken.IsCancellationRequested)
            {
                var query = _queryGenerator.GenerateQuery();
                await context.SimulateUserActionAsync(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var predictedHit = await _mlPredictionService.PredictCacheHitAsync(query);
                    var cacheResult = await _cacheProvider.TryGetAsync(query, cancellationToken);
                    sw.Stop();

                    samples.Add(new PerformanceSample
                    {
                        ResponseTime = sw.ElapsedMilliseconds,
                        CacheHit = cacheResult != null,
                        PredictionAccurate = predictedHit == (cacheResult != null),
                        MemoryUsage = Process.GetCurrentProcess().WorkingSet64
                    });
                });
            }

            metrics.CalculateFromSamples(samples);
            return metrics;
        }

        private async Task TestConcurrentUsers(
            StressTestConfig config,
            BaselineMetrics baseline,
            ILoadTestContext context,
            StressTestReport report,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Testing concurrent user capacity...");
            var maxUsers = config.InitialUsers;
            var stable = true;

            while (stable && !cancellationToken.IsCancellationRequested)
            {
                var userMetrics = await RunConcurrentUserTest(maxUsers, context, cancellationToken);
                
                stable = IsPerformanceStable(userMetrics, baseline, config.StabilityThreshold);
                if (stable)
                {
                    maxUsers = (int)(maxUsers * config.LoadIncreaseFactor);
                    report.ConcurrencyResults.Add(new ConcurrencyTestResult
                    {
                        Users = maxUsers,
                        AverageResponseTime = userMetrics.AverageResponseTime,
                        ErrorRate = userMetrics.ErrorRate,
                        Stable = true
                    });
                }
                else
                {
                    report.MaxStableUsers = maxUsers;
                    report.ConcurrencyBoundary = new PerformanceBoundary
                    {
                        Metric = "Concurrent Users",
                        Value = maxUsers,
                        LimitingFactor = DetermineLimitingFactor(userMetrics, baseline)
                    };
                }
            }
        }

        private async Task TestCacheCapacity(
            StressTestConfig config,
            BaselineMetrics baseline,
            ILoadTestContext context,
            StressTestReport report,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Testing cache capacity limits...");
            var uniqueItems = 1000;
            var stable = true;

            while (stable && !cancellationToken.IsCancellationRequested)
            {
                var cacheMetrics = await RunCacheCapacityTest(uniqueItems, context, cancellationToken);
                
                stable = IsCachePerformanceStable(cacheMetrics, baseline, config.StabilityThreshold);
                if (stable)
                {
                    uniqueItems = (int)(uniqueItems * config.LoadIncreaseFactor);
                    report.CacheResults.Add(new CacheTestResult
                    {
                        UniqueItems = uniqueItems,
                        HitRate = cacheMetrics.HitRate,
                        MemoryUsage = cacheMetrics.MemoryUsage,
                        Stable = true
                    });
                }
                else
                {
                    report.MaxCacheCapacity = uniqueItems;
                    report.CacheBoundary = new PerformanceBoundary
                    {
                        Metric = "Cache Capacity",
                        Value = uniqueItems,
                        LimitingFactor = DetermineLimitingFactor(cacheMetrics, baseline)
                    };
                }
            }
        }

        private async Task TestMLPredictionLoad(
            StressTestConfig config,
            BaselineMetrics baseline,
            ILoadTestContext context,
            StressTestReport report,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Testing ML prediction capacity...");
            var predictionsPerSecond = 10;
            var stable = true;

            while (stable && !cancellationToken.IsCancellationRequested)
            {
                var mlMetrics = await RunMLLoadTest(predictionsPerSecond, context, cancellationToken);
                
                stable = IsMLPerformanceStable(mlMetrics, baseline, config.StabilityThreshold);
                if (stable)
                {
                    predictionsPerSecond = (int)(predictionsPerSecond * config.LoadIncreaseFactor);
                    report.MLResults.Add(new MLTestResult
                    {
                        PredictionsPerSecond = predictionsPerSecond,
                        Accuracy = mlMetrics.PredictionAccuracy,
                        LatencyMs = mlMetrics.PredictionLatency,
                        Stable = true
                    });
                }
                else
                {
                    report.MaxMLPredictionRate = predictionsPerSecond;
                    report.MLBoundary = new PerformanceBoundary
                    {
                        Metric = "ML Predictions/s",
                        Value = predictionsPerSecond,
                        LimitingFactor = DetermineLimitingFactor(mlMetrics, baseline)
                    };
                }
            }
        }

        private async Task TestMitigationEffectiveness(
            StressTestConfig config,
            BaselineMetrics baseline,
            ILoadTestContext context,
            StressTestReport report,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Testing mitigation effectiveness...");
            var loadLevel = config.InitialUsers;
            var effective = true;

            while (effective && !cancellationToken.IsCancellationRequested)
            {
                var mitigationMetrics = await RunMitigationTest(loadLevel, context, cancellationToken);
                
                effective = IsMitigationEffective(mitigationMetrics, baseline, config.StabilityThreshold);
                if (effective)
                {
                    loadLevel = (int)(loadLevel * config.LoadIncreaseFactor);
                    report.MitigationResults.Add(new MitigationTestResult
                    {
                        LoadLevel = loadLevel,
                        ResponseTimeReduction = mitigationMetrics.ResponseTimeReduction,
                        ErrorRateReduction = mitigationMetrics.ErrorRateReduction,
                        Effective = true
                    });
                }
                else
                {
                    report.MaxMitigationLoad = loadLevel;
                    report.MitigationBoundary = new PerformanceBoundary
                    {
                        Metric = "Mitigation Load",
                        Value = loadLevel,
                        LimitingFactor = DetermineLimitingFactor(mitigationMetrics, baseline)
                    };
                }
            }
        }

        private void DetermineSystemBoundaries(StressTestReport report)
        {
            // Analyze all test results to determine overall system boundaries
            report.SystemBoundaries = new Dictionary<string, double>
            {
                ["MaxConcurrentUsers"] = report.MaxStableUsers,
                ["MaxCacheItems"] = report.MaxCacheCapacity,
                ["MaxPredictionsPerSecond"] = report.MaxMLPredictionRate,
                ["MaxMitigatedLoad"] = report.MaxMitigationLoad
            };

            // Generate recommendations based on findings
            GenerateRecommendations(report);
        }

        private void GenerateRecommendations(StressTestReport report)
        {
            if (report.ConcurrencyBoundary?.LimitingFactor == "Memory")
            {
                report.Recommendations.Add("Consider increasing available memory or optimizing memory usage");
            }
            
            if (report.CacheBoundary?.LimitingFactor == "Eviction Rate")
            {
                report.Recommendations.Add("Review cache eviction policy or increase cache size");
            }
            
            if (report.MLBoundary?.LimitingFactor == "Accuracy")
            {
                report.Recommendations.Add("ML model may need retraining or optimization for higher loads");
            }
            
            if (report.MitigationBoundary?.LimitingFactor == "Response Time")
            {
                report.Recommendations.Add("Consider adding more aggressive mitigation strategies for extreme loads");
            }
        }
    }

    public class StressTestConfig
    {
        public int InitialUsers { get; set; } = 10;
        public double LoadIncreaseFactor { get; set; } = 1.5;
        public double StabilityThreshold { get; set; } = 0.2; // 20% deviation
        public TimeSpan SamplingPeriod { get; set; } = TimeSpan.FromMinutes(1);
    }

    public class StressTestReport
    {
        public BaselineMetrics BaselineMetrics { get; set; }
        public int MaxStableUsers { get; set; }
        public int MaxCacheCapacity { get; set; }
        public int MaxMLPredictionRate { get; set; }
        public int MaxMitigationLoad { get; set; }
        public List<ConcurrencyTestResult> ConcurrencyResults { get; set; } = new();
        public List<CacheTestResult> CacheResults { get; set; } = new();
        public List<MLTestResult> MLResults { get; set; } = new();
        public List<MitigationTestResult> MitigationResults { get; set; } = new();
        public PerformanceBoundary ConcurrencyBoundary { get; set; }
        public PerformanceBoundary CacheBoundary { get; set; }
        public PerformanceBoundary MLBoundary { get; set; }
        public PerformanceBoundary MitigationBoundary { get; set; }
        public Dictionary<string, double> SystemBoundaries { get; set; }
        public List<string> Recommendations { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class PerformanceBoundary
    {
        public string Metric { get; set; }
        public double Value { get; set; }
        public string LimitingFactor { get; set; }
    }

    public class BaselineMetrics
    {
        public double AverageResponseTime { get; set; }
        public double ErrorRate { get; set; }
        public double CacheHitRate { get; set; }
        public double PredictionAccuracy { get; set; }
        public double MemoryUsage { get; set; }

        public void CalculateFromSamples(IEnumerable<PerformanceSample> samples)
        {
            var samplesList = samples.ToList();
            if (!samplesList.Any()) return;

            AverageResponseTime = samplesList.Average(s => s.ResponseTime);
            ErrorRate = samplesList.Count(s => s.ResponseTime > 5000) / (double)samplesList.Count;
            CacheHitRate = samplesList.Count(s => s.CacheHit) / (double)samplesList.Count;
            PredictionAccuracy = samplesList.Count(s => s.PredictionAccurate) / (double)samplesList.Count;
            MemoryUsage = samplesList.Average(s => s.MemoryUsage);
        }
    }

    public class PerformanceSample
    {
        public double ResponseTime { get; set; }
        public bool CacheHit { get; set; }
        public bool PredictionAccurate { get; set; }
        public long MemoryUsage { get; set; }
    }
}