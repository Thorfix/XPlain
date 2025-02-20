using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class AutomaticCacheOptimizer : IAutomaticCacheOptimizer
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly ICacheMonitoringService _monitoringService;
        private readonly ILogger<AutomaticCacheOptimizer> _logger;
        private readonly Dictionary<string, double> _baselineThresholds;
        private readonly double _maxThresholdAdjustment = 0.2; // Maximum 20% adjustment
        private readonly TimeSpan _optimizationCooldown = TimeSpan.FromMinutes(5);
        private DateTime _lastOptimization = DateTime.MinValue;

        public AutomaticCacheOptimizer(
            ICacheProvider cacheProvider,
            ICacheMonitoringService monitoringService,
            ILogger<AutomaticCacheOptimizer> logger)
        {
            _cacheProvider = cacheProvider;
            _monitoringService = monitoringService;
            _logger = logger;
            _baselineThresholds = new Dictionary<string, double>();
        }

        public async Task OptimizeAsync(PredictionResult prediction)
        {
            if (DateTime.UtcNow - _lastOptimization < _optimizationCooldown)
            {
                return;
            }

            if (prediction.Confidence < 0.7)
            {
                _logger.LogInformation("Skipping optimization due to low prediction confidence: {Confidence}", prediction.Confidence);
                return;
            }

            try
            {
                var currentMetrics = await _monitoringService.GetPerformanceMetricsAsync();
                var currentSize = await _cacheProvider.GetCacheSizeAsync();
                var maxSize = await _cacheProvider.GetMaxCacheSizeAsync();

                // Determine optimization strategy based on prediction
                if (prediction.DetectedPattern != null)
                {
                    switch (prediction.DetectedPattern.Type)
                    {
                        case "LowHitRate":
                            await HandleLowHitRateAsync(currentMetrics, prediction);
                            break;
                        case "HighMemoryUsage":
                            await HandleHighMemoryUsageAsync(currentSize, maxSize, prediction);
                            break;
                        case "SlowResponse":
                            await HandleSlowResponseAsync(currentMetrics, prediction);
                            break;
                    }
                }

                _lastOptimization = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache optimization");
            }
        }

        public async Task AdjustEvictionPolicyAsync(TrendAnalysis trends)
        {
            try
            {
                var currentPolicy = await _cacheProvider.GetEvictionPolicyAsync();
                ICacheEvictionPolicy newPolicy = currentPolicy;

                switch (trends.Trend)
                {
                    case TrendDirection.Increasing when trends.Volatility > 0.5:
                        // High volatility with increasing trend - use LRU for quick adaptation
                        newPolicy = new LRUEvictionPolicy();
                        break;
                    case TrendDirection.Stable when trends.Seasonality > 0.7:
                        // Strong seasonality - use LFU to maintain frequently accessed items
                        newPolicy = new LFUEvictionPolicy();
                        break;
                    case TrendDirection.Decreasing:
                        // Decreasing trend - use FIFO to maintain fresh data
                        newPolicy = new FIFOEvictionPolicy();
                        break;
                }

                if (newPolicy.GetType() != currentPolicy.GetType())
                {
                    await _cacheProvider.SetEvictionPolicyAsync(newPolicy);
                    _logger.LogInformation(
                        "Adjusted eviction policy from {OldPolicy} to {NewPolicy}",
                        currentPolicy.GetType().Name,
                        newPolicy.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adjusting eviction policy");
            }
        }

        public async Task UpdateCacheWarningThresholdsAsync(List<PredictedAlert> alerts)
        {
            if (!alerts.Any()) return;

            try
            {
                var currentThresholds = await _monitoringService.GetCurrentThresholdsAsync();

                // Store baseline thresholds if not already stored
                if (!_baselineThresholds.Any())
                {
                    _baselineThresholds["MinHitRatio"] = currentThresholds.MinHitRatio;
                    _baselineThresholds["MaxMemoryUsageMB"] = currentThresholds.MaxMemoryUsageMB;
                    _baselineThresholds["MaxResponseTimeMs"] = currentThresholds.MaxResponseTimeMs;
                }

                foreach (var alert in alerts.Where(a => a.Confidence > 0.8))
                {
                    double adjustment = DetermineThresholdAdjustment(alert);
                    
                    switch (alert.Metric)
                    {
                        case "CacheHitRate":
                            currentThresholds.MinHitRatio = AdjustThreshold(
                                currentThresholds.MinHitRatio,
                                _baselineThresholds["MinHitRatio"],
                                adjustment);
                            break;
                        case "MemoryUsage":
                            currentThresholds.MaxMemoryUsageMB = AdjustThreshold(
                                currentThresholds.MaxMemoryUsageMB,
                                _baselineThresholds["MaxMemoryUsageMB"],
                                adjustment);
                            break;
                        case "AverageResponseTime":
                            currentThresholds.MaxResponseTimeMs = AdjustThreshold(
                                currentThresholds.MaxResponseTimeMs,
                                _baselineThresholds["MaxResponseTimeMs"],
                                adjustment);
                            break;
                    }
                }

                await _monitoringService.UpdateThresholdsAsync(currentThresholds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cache warning thresholds");
            }
        }

        public async Task PrewarmFrequentItemsAsync(Dictionary<string, double> hitRates)
        {
            try
            {
                var frequentItems = hitRates
                    .Where(hr => hr.Value > 0.8) // Items with high hit rates
                    .Select(hr => hr.Key)
                    .ToList();

                if (!frequentItems.Any())
                {
                    return;
                }

                var currentSize = await _cacheProvider.GetCacheSizeAsync();
                var maxSize = await _cacheProvider.GetMaxCacheSizeAsync();
                var availableSpace = maxSize - currentSize;

                if (availableSpace > maxSize * 0.1) // Ensure at least 10% free space
                {
                    foreach (var item in frequentItems)
                    {
                        await _cacheProvider.PrewarmItemAsync(item);
                    }
                    
                    _logger.LogInformation(
                        "Prewarmed {Count} frequent items in cache",
                        frequentItems.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error prewarming frequent items");
            }
        }

        private async Task HandleLowHitRateAsync(
            Dictionary<string, double> currentMetrics,
            PredictionResult prediction)
        {
            var hitRate = currentMetrics["CacheHitRate"];
            if (hitRate < 0.6) // Critical low hit rate
            {
                await _cacheProvider.ExpandCacheSizeAsync(0.2); // Increase by 20%
                _logger.LogInformation("Increased cache size by 20% due to low hit rate prediction");
            }
            else if (prediction.TimeToImpact < TimeSpan.FromMinutes(15))
            {
                await _cacheProvider.ExpandCacheSizeAsync(0.1); // Increase by 10%
                _logger.LogInformation("Proactively increased cache size by 10%");
            }
        }

        private async Task HandleHighMemoryUsageAsync(
            long currentSize,
            long maxSize,
            PredictionResult prediction)
        {
            var usageRatio = (double)currentSize / maxSize;
            if (usageRatio > 0.9) // Critical high memory usage
            {
                await _cacheProvider.TrimCacheAsync(0.2); // Reduce by 20%
                _logger.LogInformation("Reduced cache size by 20% due to high memory usage");
            }
            else if (prediction.TimeToImpact < TimeSpan.FromMinutes(15))
            {
                await _cacheProvider.TrimCacheAsync(0.1); // Reduce by 10%
                _logger.LogInformation("Proactively reduced cache size by 10%");
            }
        }

        private async Task HandleSlowResponseAsync(
            Dictionary<string, double> currentMetrics,
            PredictionResult prediction)
        {
            var responseTime = currentMetrics["AverageResponseTime"];
            if (responseTime > 1000) // Response time > 1 second
            {
                await _cacheProvider.OptimizeIndexesAsync();
                _logger.LogInformation("Optimized cache indexes due to slow response time");
            }
            else if (prediction.TimeToImpact < TimeSpan.FromMinutes(15))
            {
                await _cacheProvider.PrewarmCacheAsync();
                _logger.LogInformation("Proactively prewarmed cache");
            }
        }

        private double DetermineThresholdAdjustment(PredictedAlert alert)
        {
            var severityMultiplier = alert.Severity switch
            {
                "Critical" => 0.2,
                "Warning" => 0.1,
                _ => 0.05
            };

            var urgencyMultiplier = alert.TimeToImpact < TimeSpan.FromMinutes(15) ? 1.5 : 1.0;
            
            return severityMultiplier * urgencyMultiplier;
        }

        private double AdjustThreshold(double current, double baseline, double adjustment)
        {
            var maxAdjustment = baseline * _maxThresholdAdjustment;
            var proposedAdjustment = current * adjustment;
            var limitedAdjustment = Math.Min(Math.Abs(proposedAdjustment), maxAdjustment) 
                                  * Math.Sign(proposedAdjustment);
            
            return Math.Max(baseline * 0.5, Math.Min(baseline * 1.5, current + limitedAdjustment));
        }
    }
}