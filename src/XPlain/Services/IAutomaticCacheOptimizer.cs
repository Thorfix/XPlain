using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IAutomaticCacheOptimizer
    {
        Task OptimizeAsync(PredictionResult prediction);
        Task AdjustEvictionPolicyAsync(TrendAnalysis trends);
        Task UpdateCacheWarningThresholdsAsync(List<PredictedAlert> alerts);
        Task PrewarmFrequentItemsAsync(Dictionary<string, double> hitRates);
        Task<OptimizationMetrics> GetOptimizationMetricsAsync();
        Task SetEmergencyOverrideAsync(bool enabled);
    }
}