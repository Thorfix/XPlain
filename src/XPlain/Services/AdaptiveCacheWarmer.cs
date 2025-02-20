using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class AdaptiveCacheWarmer : IHostedService
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _mlPredictionService;
        private readonly MLModelTrainingService _mlModelTrainingService;
        private readonly ILogger<AdaptiveCacheWarmer> _logger;
        private readonly CacheMonitoringService _monitoringService;
        private Timer? _warmupTimer;
        private Timer? _trainingTimer;
        private readonly PreWarmingMetrics _metrics;

        public AdaptiveCacheWarmer(
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService,
            MLModelTrainingService mlModelTrainingService,
            CacheMonitoringService monitoringService,
            ILogger<AdaptiveCacheWarmer> logger)
        {
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
            _mlModelTrainingService = mlModelTrainingService;
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
                var trainingData = await PrepareTrainingData(stats);
                
                await _mlModelTrainingService.TrainModelAsync(
                    trainingData,
                    new ModelTrainingParameters
                    {
                        IncludeTimeFeatures = true,
                        IncludeUserActivityPatterns = true,
                        IncludeQueryPatterns = true,
                        OptimizeForResourceUsage = true
                    }
                );

                // Update the prediction service with the new model
                await _mlPredictionService.UpdateModelAsync("cache_prediction_model.zip");
                
                _logger.LogInformation("Successfully trained and updated ML model");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ML model training");
            }
        }

        private async Task<List<CacheTrainingData>> PrepareTrainingData(CacheStats stats)
        {
            var trainingData = new List<CacheTrainingData>();
            
            foreach (var query in stats.TopQueries)
            {
                var performanceMetrics = stats.PerformanceByQueryType.GetValueOrDefault(query.Query);
                
                trainingData.Add(new CacheTrainingData
                {
                    Query = query.Query,
                    Frequency = query.Frequency,
                    TimeOfDay = query.Query.GetHashCode() % 24, // Simplified for example
                    DayOfWeek = (int)DateTime.UtcNow.DayOfWeek,
                    ResponseTime = stats.AverageResponseTimes.GetValueOrDefault(query.Query),
                    CacheHitRate = performanceMetrics?.CachedResponseTime ?? 0,
                    ResourceUsage = _metrics.AverageResourceUsage
                });
            }

            return trainingData;
        }

        private async Task ExecuteWarmingCycle(CancellationToken cancellationToken)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startMemory = GC.GetTotalMemory(false);
                var baselineHitRate = _cacheProvider.GetCacheStats().HitRatio;
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