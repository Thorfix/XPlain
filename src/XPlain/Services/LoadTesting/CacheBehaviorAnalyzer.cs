using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting
{
    public class CacheBehaviorAnalyzer
    {
        private readonly MLPredictionService _mlPredictionService;
        private readonly ICacheProvider _cacheProvider;
        private readonly ConcurrentDictionary<string, BehaviorMetrics> _metrics;

        public CacheBehaviorAnalyzer(MLPredictionService mlPredictionService, ICacheProvider cacheProvider)
        {
            _mlPredictionService = mlPredictionService;
            _cacheProvider = cacheProvider;
            _metrics = new ConcurrentDictionary<string, BehaviorMetrics>();
        }

        public async Task<BehaviorComparisonReport> AnalyzeBehavior(string query, string pattern)
        {
            // Get ML prediction before cache access
            var predictedHit = await _mlPredictionService.PredictCacheHitAsync(query);
            var predictedLatency = await _mlPredictionService.PredictResponseTimeAsync(query);
            
            // Measure actual cache behavior
            var startTime = DateTime.UtcNow;
            var actualResult = await _cacheProvider.TryGetAsync(query, default);
            var actualLatency = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var actualHit = actualResult != null;

            // Record metrics
            var metrics = new BehaviorMetrics
            {
                PredictedHit = predictedHit,
                ActualHit = actualHit,
                PredictedLatency = predictedLatency,
                ActualLatency = actualLatency,
                Pattern = pattern,
                Timestamp = DateTime.UtcNow
            };

            _metrics.AddOrUpdate(
                $"{pattern}_{DateTime.UtcNow.Ticks}",
                metrics,
                (_, _) => metrics
            );

            return new BehaviorComparisonReport
            {
                Pattern = pattern,
                PredictionAccuracy = predictedHit == actualHit,
                LatencyPredictionError = Math.Abs(predictedLatency - actualLatency),
                ActualMetrics = new CacheMetrics
                {
                    HitRate = actualHit ? 1 : 0,
                    Latency = actualLatency,
                    TimeStamp = DateTime.UtcNow
                },
                PredictedMetrics = new CacheMetrics
                {
                    HitRate = predictedHit ? 1 : 0,
                    Latency = predictedLatency,
                    TimeStamp = DateTime.UtcNow
                }
            };
        }

        public async Task<BehaviorSummaryReport> GenerateSummaryReport()
        {
            var summary = new BehaviorSummaryReport
            {
                TotalQueries = _metrics.Count,
                PatternAnalysis = new ConcurrentDictionary<string, PatternMetrics>()
            };

            foreach (var metric in _metrics)
            {
                var pattern = metric.Value.Pattern;
                summary.PatternAnalysis.AddOrUpdate(
                    pattern,
                    new PatternMetrics
                    {
                        TotalQueries = 1,
                        CorrectPredictions = metric.Value.PredictedHit == metric.Value.ActualHit ? 1 : 0,
                        AverageLatencyError = Math.Abs(metric.Value.PredictedLatency - metric.Value.ActualLatency),
                        ActualHitRate = metric.Value.ActualHit ? 1 : 0,
                        PredictedHitRate = metric.Value.PredictedHit ? 1 : 0,
                        AverageActualLatency = metric.Value.ActualLatency,
                        AveragePredictedLatency = metric.Value.PredictedLatency
                    },
                    (_, existing) =>
                    {
                        existing.TotalQueries++;
                        if (metric.Value.PredictedHit == metric.Value.ActualHit)
                            existing.CorrectPredictions++;
                        existing.AverageLatencyError = (existing.AverageLatencyError * (existing.TotalQueries - 1) +
                            Math.Abs(metric.Value.PredictedLatency - metric.Value.ActualLatency)) / existing.TotalQueries;
                        existing.ActualHitRate = (existing.ActualHitRate * (existing.TotalQueries - 1) +
                            (metric.Value.ActualHit ? 1 : 0)) / existing.TotalQueries;
                        existing.PredictedHitRate = (existing.PredictedHitRate * (existing.TotalQueries - 1) +
                            (metric.Value.PredictedHit ? 1 : 0)) / existing.TotalQueries;
                        existing.AverageActualLatency = (existing.AverageActualLatency * (existing.TotalQueries - 1) +
                            metric.Value.ActualLatency) / existing.TotalQueries;
                        existing.AveragePredictedLatency = (existing.AveragePredictedLatency * (existing.TotalQueries - 1) +
                            metric.Value.PredictedLatency) / existing.TotalQueries;
                        return existing;
                    }
                );
            }

            GenerateRecommendations(summary);
            return summary;
        }

        private void GenerateRecommendations(BehaviorSummaryReport summary)
        {
            foreach (var pattern in summary.PatternAnalysis)
            {
                var metrics = pattern.Value;
                
                // Analyze prediction accuracy
                if (metrics.CorrectPredictions / (double)metrics.TotalQueries < 0.8)
                {
                    summary.Recommendations.Add($"ML model needs retraining for {pattern.Key} pattern - " +
                        $"prediction accuracy is {metrics.CorrectPredictions / (double)metrics.TotalQueries:P2}");
                }

                // Analyze latency predictions
                if (metrics.AverageLatencyError > 100) // More than 100ms average error
                {
                    summary.Recommendations.Add($"High latency prediction error for {pattern.Key} pattern - " +
                        $"average error is {metrics.AverageLatencyError:F2}ms");
                }

                // Analyze hit rate predictions
                var hitRateError = Math.Abs(metrics.ActualHitRate - metrics.PredictedHitRate);
                if (hitRateError > 0.2) // More than 20% difference
                {
                    summary.Recommendations.Add($"Hit rate predictions for {pattern.Key} pattern need improvement - " +
                        $"prediction error is {hitRateError:P2}");
                }
            }
        }
    }

    public class BehaviorMetrics
    {
        public bool PredictedHit { get; set; }
        public bool ActualHit { get; set; }
        public double PredictedLatency { get; set; }
        public double ActualLatency { get; set; }
        public string Pattern { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class BehaviorComparisonReport
    {
        public string Pattern { get; set; }
        public bool PredictionAccuracy { get; set; }
        public double LatencyPredictionError { get; set; }
        public CacheMetrics ActualMetrics { get; set; }
        public CacheMetrics PredictedMetrics { get; set; }
    }

    public class BehaviorSummaryReport
    {
        public int TotalQueries { get; set; }
        public ConcurrentDictionary<string, PatternMetrics> PatternAnalysis { get; set; }
            = new ConcurrentDictionary<string, PatternMetrics>();
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    public class PatternMetrics
    {
        public int TotalQueries { get; set; }
        public int CorrectPredictions { get; set; }
        public double AverageLatencyError { get; set; }
        public double ActualHitRate { get; set; }
        public double PredictedHitRate { get; set; }
        public double AverageActualLatency { get; set; }
        public double AveragePredictedLatency { get; set; }
    }

    public class CacheMetrics
    {
        public double HitRate { get; set; }
        public double Latency { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}