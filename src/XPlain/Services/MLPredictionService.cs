using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace XPlain.Services
{
    public class MLPredictionService
    {
        private readonly MLContext _mlContext;
        private readonly ILogger<MLPredictionService> _logger;
        private readonly MLModelTrainingService _trainingService;
        private ITransformer? _model;
        private ModelVersion? _activeVersion;

        public MLPredictionService(
            ILogger<MLPredictionService> logger,
            MLModelTrainingService trainingService)
        {
            _mlContext = new MLContext(seed: 1);
            _logger = logger;
            _trainingService = trainingService;
        }

        public async Task<double> PredictQueryValueAsync(string key)
        {
            try
            {
                // This would normally use a trained ML model
                // For now, return a simple heuristic score
                var parts = key.Split(':');
                var baseScore = 1.0;

                if (parts.Length > 1)
                {
                    // Give higher scores to more specific queries
                    baseScore *= parts.Length;
                }

                // Add time-based weighting
                var hourOfDay = DateTime.UtcNow.Hour;
                var timeWeight = 1.0;

                // Assume business hours (8-18) are more important
                if (hourOfDay >= 8 && hourOfDay <= 18)
                {
                    timeWeight = 1.5;
                }

                return baseScore * timeWeight;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error predicting query value for key: {Key}", key);
                return 0.0;
            }
        }

        public async Task<Dictionary<string, DateTime>> PredictOptimalTimingsAsync(List<string> keys)
        {
            var result = new Dictionary<string, DateTime>();
            var now = DateTime.UtcNow;

            foreach (var key in keys)
            {
                try
                {
                    // This would normally use ML predictions
                    // For now, distribute cache warming across the next 24 hours
                    var hourOffset = Math.Abs(key.GetHashCode() % 24);
                    result[key] = now.AddHours(hourOffset);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error predicting timing for key: {Key}", key);
                    result[key] = now.AddHours(1); // Default to 1 hour from now
                }
            }

            return result;
        }

        public async Task<Dictionary<string, double>> PredictCacheUsageAsync(
            List<(string Query, int Frequency)> historicalQueries,
            DateTime targetTime)
        {
            try
            {
                var predictions = new Dictionary<string, double>();

                foreach (var (query, frequency) in historicalQueries)
                {
                    // This would normally use the trained ML model
                    // For now, use simple time-based heuristics
                    var hourOfDay = targetTime.Hour;
                    var dayOfWeek = (int)targetTime.DayOfWeek;
                    
                    var baseScore = frequency / 100.0;
                    var timeMultiplier = 1.0;

                    // Business hours adjustment
                    if (hourOfDay >= 8 && hourOfDay <= 18 && dayOfWeek >= 1 && dayOfWeek <= 5)
                    {
                        timeMultiplier = 1.5;
                    }

                    predictions[query] = baseScore * timeMultiplier;
                }

                return predictions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error predicting cache usage");
                return new Dictionary<string, double>();
            }
        }

        public async Task UpdateModelAsync(string modelVersionId)
        {
            try
            {
                var version = await _trainingService.GetModelVersion(modelVersionId);
                if (version == null)
                {
                    throw new InvalidOperationException($"Model version {modelVersionId} not found");
                }

                // Load the trained model
                _model = await Task.Run(() => _mlContext.Model.Load(version.FilePath, out var _));
                _activeVersion = version;
                _logger.LogInformation("Successfully loaded ML model version {Version}", modelVersionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading ML model version {Version}", modelVersionId);
                throw;
            }
        }

        public async Task<ModelVersion?> GetActiveModelVersion()
        {
            return _activeVersion;
        }

        public async Task<bool> FallbackToPreviousVersion()
        {
            try
            {
                var versions = await _trainingService.GetModelVersions();
                var previousVersion = versions
                    .Where(v => v != _activeVersion && v.Status == "trained")
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefault();

                if (previousVersion != null)
                {
                    await UpdateModelAsync(previousVersion.Id);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error falling back to previous model version");
                return false;
            }
        }

        private class QueryPredictionData
        {
            [LoadColumn(0)]
            public string QueryKey { get; set; } = "";

            [LoadColumn(1)]
            public float Frequency { get; set; }

            [LoadColumn(2)]
            public float HourOfDay { get; set; }

            [LoadColumn(3)]
            public float DayOfWeek { get; set; }

            [LoadColumn(4)]
            public float ResponseTime { get; set; }

            [LoadColumn(5)]
            public float CacheHitRate { get; set; }
        }

        private class QueryPrediction
        {
            [ColumnName("PredictedValue")]
            public float PredictedValue { get; set; }
        }
    }
}