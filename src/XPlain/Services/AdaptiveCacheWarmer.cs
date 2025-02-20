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
        private readonly ILogger<AdaptiveCacheWarmer> _logger;
        private readonly CacheMonitoringService _monitoringService;
        private Timer? _warmupTimer;

        public AdaptiveCacheWarmer(
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService,
            CacheMonitoringService monitoringService,
            ILogger<AdaptiveCacheWarmer> logger)
        {
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
            _monitoringService = monitoringService;
            _logger = logger;
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
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Adaptive Cache Warmer");
            _warmupTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async Task ExecuteWarmingCycle(CancellationToken cancellationToken)
        {
            try
            {
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
                        await _cacheProvider.PreWarmBatchAsync(batch, group.Key);
                        
                        // Monitor and log performance metrics
                        await _monitoringService.RecordPreWarmingMetrics(
                            batch.Count(),
                            group.Key,
                            DateTime.UtcNow
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