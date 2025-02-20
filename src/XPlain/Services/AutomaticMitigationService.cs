using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class AutomaticMitigationService
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _predictionService;
        private readonly ICacheMonitoringService _monitoringService;
        private readonly Dictionary<string, double> _resourceAllocation;

        public AutomaticMitigationService(
            ICacheProvider cacheProvider,
            MLPredictionService predictionService,
            ICacheMonitoringService monitoringService)
        {
            _cacheProvider = cacheProvider;
            _predictionService = predictionService;
            _monitoringService = monitoringService;
            _resourceAllocation = new Dictionary<string, double>();
        }

        public async Task ApplyMitigations()
        {
            var predictions = await _predictionService.PredictPerformanceMetrics();
            var alerts = await _predictionService.GetPredictedAlerts();

            foreach (var alert in alerts)
            {
                switch (alert.Metric.ToLower())
                {
                    case "cachehitrate" when alert.PredictedValue < 0.7:
                        await PreWarmCache(alert);
                        break;
                    case "memoryusage" when alert.PredictedValue > 90:
                        await AdjustEvictionPolicy(alert);
                        break;
                    case "averageresponsetime" when alert.PredictedValue > 200:
                        await AllocateAdditionalResources(alert);
                        break;
                }
            }
        }

        private async Task PreWarmCache(PredictedAlert alert)
        {
            if (_cacheProvider is FileBasedCacheProvider provider)
            {
                var frequentKeys = await provider.GetMostFrequentKeys(100);
                foreach (var key in frequentKeys)
                {
                    // Ensure key is in cache and data is fresh
                    if (!await provider.IsKeyFresh(key))
                    {
                        await provider.PreWarmKey(key);
                    }
                }

                await _monitoringService.LogMaintenanceEventAsync(
                    "CachePreWarming",
                    "Info",
                    TimeSpan.FromSeconds(30),
                    new Dictionary<string, object>
                    {
                        ["keysWarmed"] = frequentKeys.Count,
                        ["triggerAlert"] = alert.Metric
                    });
            }
        }

        private async Task AdjustEvictionPolicy(PredictedAlert alert)
        {
            if (_cacheProvider is FileBasedCacheProvider provider)
            {
                // Switch to more aggressive eviction policy
                var currentPolicy = provider.EvictionPolicy;
                if (currentPolicy is ICacheEvictionPolicy policy)
                {
                    await policy.UpdateEvictionThreshold(0.85); // More aggressive eviction
                    await policy.UpdatePressureThreshold(0.75); // Lower pressure threshold
                }

                await _monitoringService.LogMaintenanceEventAsync(
                    "EvictionPolicyAdjustment",
                    "Info",
                    TimeSpan.FromSeconds(5),
                    new Dictionary<string, object>
                    {
                        ["newThreshold"] = 0.85,
                        ["triggerAlert"] = alert.Metric
                    });
            }
        }

        private async Task AllocateAdditionalResources(PredictedAlert alert)
        {
            if (_cacheProvider is FileBasedCacheProvider provider)
            {
                // Increase memory allocation
                var currentAllocation = _resourceAllocation.GetValueOrDefault("memory", 100);
                var newAllocation = Math.Min(currentAllocation * 1.2, 200); // 20% increase, max 200%
                _resourceAllocation["memory"] = newAllocation;

                await provider.UpdateResourceAllocation(new Dictionary<string, double>
                {
                    ["memory"] = newAllocation,
                    ["cpu"] = _resourceAllocation.GetValueOrDefault("cpu", 100)
                });

                await _monitoringService.LogMaintenanceEventAsync(
                    "ResourceAllocation",
                    "Info",
                    TimeSpan.FromSeconds(10),
                    new Dictionary<string, object>
                    {
                        ["memoryAllocation"] = newAllocation,
                        ["triggerAlert"] = alert.Metric
                    });
            }
        }
    }
}