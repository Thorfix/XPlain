using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Hosting;

namespace XPlain.Services
{
    public class AdaptiveCacheWarmer : IHostedService, IDisposable
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _mlPredictionService;
        private readonly MLModelTrainingService _mlModelTrainingService;
        private readonly ResourceMonitor _resourceMonitor;
        private readonly ILogger<AdaptiveCacheWarmer> _logger;
        private readonly CacheMonitoringService _monitoringService;
        private Timer? _warmupTimer;
        private Timer? _trainingTimer;
        private readonly PreWarmingMetrics _metrics;

        public AdaptiveCacheWarmer(
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService,
            MLModelTrainingService mlModelTrainingService,
            ResourceMonitor resourceMonitor,
            CacheMonitoringService monitoringService,
            ILogger<AdaptiveCacheWarmer> logger)
        {
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
            _mlModelTrainingService = mlModelTrainingService;
            _resourceMonitor = resourceMonitor;
            _monitoringService = monitoringService;
            _logger = logger;
            _metrics = new PreWarmingMetrics();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Adaptive Cache Warmer");
            await UpdateWarmingStrategy(cancellationToken);
            
            // Initialize timer for periodic strategy updates
            _warmupTimer = new Timer(
                async _ => await ExecuteWarmingCycle(cancellationToken),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15)
            );

            // Initialize timer for ML model training
            _trainingTimer = new Timer(
                async _ => await TrainMLModel(cancellationToken),
                null,
                TimeSpan.FromHours(1), // Initial delay
                TimeSpan.FromHours(24) // Train daily
            );
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Adaptive Cache Warmer");
            _warmupTimer?.Change(Timeout.Infinite, 0);
            _trainingTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async Task TrainMLModel(CancellationToken cancellationToken)
        {
            try
            {
                var stats = _cacheProvider.GetCacheStats();
                var resourceMetrics = await _resourceMonitor.GetResourceMetricsAsync();
                var trainingData = await PrepareTrainingData(stats);
                
                // Get user activity patterns
                var userPatterns = stats.TopQueries
                    .GroupBy(q => GetTimeSlot(q.Query))
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(q => q.Frequency)
                    );

                // Create enhanced training parameters
                var parameters = new ModelTrainingParameters
                {
                    IncludeTimeFeatures = true,
                    IncludeUserActivityPatterns = true,
                    IncludeQueryPatterns = true,
                    OptimizeForResourceUsage = true,
                    Features = new Dictionary<string, object>
                    {
                        ["user_patterns"] = userPatterns,
                        ["system_load"] = resourceMetrics.SystemLoad,
                        ["available_memory"] = resourceMetrics.AvailableMemoryMB,
                        ["cpu_usage"] = resourceMetrics.CpuUsagePercent,
                        ["io_operations"] = resourceMetrics.IoOperationsPerSecond
                    },
                    Weights = new Dictionary<string, double>
                    {
                        ["access_frequency"] = 0.4,
                        ["performance_impact"] = 0.3,
                        ["resource_usage"] = 0.2,
                        ["time_pattern"] = 0.1
                    }
                };

                await _mlModelTrainingService.TrainModelAsync(
                    trainingData,
                    parameters
                );

                // Update the prediction service with the new model
                await _mlPredictionService.UpdateModelAsync("cache_prediction_model.zip");
                
                _logger.LogInformation(
                    "Successfully trained and updated ML model with {QueryCount} queries and {PatternCount} time patterns",
                    trainingData.Count,
                    userPatterns.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ML model training");
            }
        }

        private async Task<List<CacheTrainingData>> PrepareTrainingData(CacheStats stats)
        {
            var trainingData = new List<CacheTrainingData>();
            var resourceMetrics = await _resourceMonitor.GetResourceMetricsAsync();
            var now = DateTime.UtcNow;
            
            foreach (var query in stats.TopQueries)
            {
                var performanceMetrics = stats.PerformanceByQueryType.GetValueOrDefault(query.Query);
                var accessStats = stats.QueryTypeStats.GetValueOrDefault(query.Query, 0);
                var timePatterns = AnalyzeTimePatterns(query.Query, stats);
                
                trainingData.Add(new CacheTrainingData
                {
                    Key = query.Query,
                    AccessFrequency = query.Frequency,
                    TimeOfDay = timePatterns.HourOfDay,
                    DayOfWeek = timePatterns.DayOfWeek,
                    UserActivityLevel = CalculateUserActivityLevel(accessStats, stats.CachedItemCount),
                    ResponseTime = stats.AverageResponseTimes.GetValueOrDefault(query.Query),
                    CacheHitRate = performanceMetrics?.CachedResponseTime ?? 0,
                    ResourceUsage = _metrics.AverageResourceUsage,
                    PerformanceGain = performanceMetrics?.PerformanceGain ?? 0,
                    Label = CalculateCacheValue(query.Frequency, performanceMetrics?.PerformanceGain ?? 0)
                });
            }

            return trainingData;
        }

        private (float HourOfDay, float DayOfWeek) AnalyzeTimePatterns(string query, CacheStats stats)
        {
            // Analyze historical access patterns to determine most frequent access times
            var history = stats.InvalidationHistory
                .Where(h => h.Time > DateTime.UtcNow.AddDays(-7))
                .ToList();

            if (history.Count > 0)
            {
                var mostFrequentHour = history
                    .GroupBy(h => h.Time.Hour)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .Key;

                var mostFrequentDay = history
                    .GroupBy(h => h.Time.DayOfWeek)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .Key;

                return (mostFrequentHour, (float)mostFrequentDay);
            }

            // Fallback to hash-based distribution if no history
            return (
                query.GetHashCode() % 24,
                Math.Abs((query.GetHashCode() / 24) % 7)
            );
        }

        private float CalculateUserActivityLevel(int queryCount, int totalItems)
        {
            if (totalItems == 0) return 0;
            return (float)queryCount / totalItems * 100;
        }

        private float CalculateCacheValue(int frequency, double performanceGain)
        {
            // Normalize and combine frequency and performance impact
            var normalizedFrequency = Math.Min(frequency / 1000.0f, 1.0f);
            var normalizedGain = (float)Math.Min(performanceGain / 100.0, 1.0);
            
            return (normalizedFrequency * 0.6f) + (normalizedGain * 0.4f);
        }

        private string GetTimeSlot(string query)
        {
            // Extract or derive time-based patterns from query
            var hash = query.GetHashCode();
            var hour = Math.Abs(hash % 24);
            return $"{hour:D2}:00";
        }

        private async Task ExecuteWarmingCycle(CancellationToken cancellationToken)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startMemory = GC.GetTotalMemory(false);
                var baselineHitRate = _cacheProvider.GetCacheStats().HitRatio;
                var resourceMetrics = await _resourceMonitor.GetResourceMetricsAsync();

                // Check if system can handle pre-warming
                if (!resourceMetrics.CanHandleAdditionalLoad(100)) // Assume 100MB baseline
                {
                    _logger.LogWarning(
                        "Skipping pre-warming cycle due to high system load: CPU {CpuUsage}%, Memory {MemoryUsage}MB",
                        resourceMetrics.CpuUsagePercent,
                        resourceMetrics.MemoryUsageMB);
                    return;
                }
                var strategy = await _cacheProvider.OptimizePreWarmingStrategyAsync();
                var candidates = await _cacheProvider.GetPreWarmCandidatesAsync();
                
                // Group candidates by priority
                var priorityGroups = candidates
                    .Where(c => strategy.KeyPriorities.ContainsKey(c.Key))
                    .GroupBy(c => strategy.KeyPriorities[c.Key])
                    .OrderByDescending(g => g.Key);

                foreach (var group in priorityGroups)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var keys = group.Select(c => c.Key).ToList();
                    var batchSize = strategy.BatchSize;
                    
                    for (int i = 0; i < keys.Count; i += batchSize)
                    {
                        // Check resource metrics before each batch
                        resourceMetrics = await _resourceMonitor.GetResourceMetricsAsync();
                        if (!resourceMetrics.CanHandleAdditionalLoad(50)) // 50MB per batch estimate
                        {
                            _logger.LogInformation(
                                "Pausing pre-warming due to resource constraints. Completed {CompletedCount}/{TotalCount} items",
                                i,
                                keys.Count);
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                            continue;
                        }

                        var batch = keys.Skip(i).Take(batchSize);
                        var success = await _cacheProvider.PreWarmBatchAsync(batch, group.Key);
                        
                        // Update metrics
                        var endMemory = GC.GetTotalMemory(false);
                        var memoryUsed = endMemory - startMemory;
                        var currentHitRate = _cacheProvider.GetCacheStats().HitRatio;
                        
                        lock (_metrics)
                        {
                            _metrics.TotalAttempts++;
                            if (success) _metrics.SuccessfulPreWarms++;
                            _metrics.AverageResourceUsage = (_metrics.AverageResourceUsage + memoryUsed) / 2;
                            _metrics.CacheHitImprovementPercent = (currentHitRate - baselineHitRate) * 100;
                            _metrics.LastUpdate = DateTime.UtcNow;
                        }
                        
                        // Monitor and log performance metrics
                        await _monitoringService.RecordPreWarmingMetrics(
                            batch.Count(),
                            group.Key,
                            DateTime.UtcNow,
                            new PreWarmingMetrics
                            {
                                TotalAttempts = _metrics.TotalAttempts,
                                SuccessfulPreWarms = _metrics.SuccessfulPreWarms,
                                AverageResourceUsage = _metrics.AverageResourceUsage,
                                CacheHitImprovementPercent = _metrics.CacheHitImprovementPercent,
                                AverageResponseTimeImprovement = CalculateResponseTimeImprovement(),
                                LastUpdate = _metrics.LastUpdate
                            }
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache warming cycle");
            }
        }

        private async Task UpdateWarmingStrategy(CancellationToken cancellationToken)
        {
            try
            {
                // Get current cache statistics
                var stats = _cacheProvider.GetCacheStats();
                
                // Use ML predictions to optimize strategy
                var predictions = await _mlPredictionService.PredictCacheUsageAsync(
                    stats.TopQueries,
                    DateTime.UtcNow
                );

                // Update warming strategy based on predictions
                var strategy = await _cacheProvider.OptimizePreWarmingStrategyAsync();
                
                _logger.LogInformation(
                    "Updated cache warming strategy: {BatchSize} items per batch, {Interval} interval",
                    strategy.BatchSize,
                    strategy.PreWarmInterval
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating warming strategy");
            }
        }
    }
}