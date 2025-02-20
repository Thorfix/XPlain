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
        private readonly MLModelValidationService _validationService;
        private readonly MetricsCollectionService _metricsService;
        private ITransformer? _model;
        private ModelVersion? _activeVersion;
        private Dictionary<string, ITransformer> _shadowModels;
        private bool _shadowModeEnabled;

        public MLPredictionService(
            ILogger<MLPredictionService> logger,
            MLModelTrainingService trainingService,
            MLModelValidationService validationService,
            MetricsCollectionService metricsService)
        {
            _mlContext = new MLContext(seed: 1);
            _logger = logger;
            _trainingService = trainingService;
            _validationService = validationService;
            _metricsService = metricsService;
            _shadowModels = new Dictionary<string, ITransformer>();
            _shadowModeEnabled = false;
        }

        public async Task<double> PredictQueryValueAsync(string key)
        {
            try
            {
                if (_model == null)
                {
                    // Fallback to heuristic if model not loaded
                    return await PredictUsingHeuristics(key);
                }

                var data = new QueryPredictionData
                {
                    QueryKey = key,
                    HourOfDay = DateTime.UtcNow.Hour,
                    DayOfWeek = (float)DateTime.UtcNow.DayOfWeek,
                    Frequency = await GetQueryFrequency(key),
                    ResponseTime = await GetAverageResponseTime(key),
                    CacheHitRate = await GetCacheHitRate(key)
                };

                var predictionEngine = _mlContext.Model.CreatePredictionEngine<QueryPredictionData, QueryPrediction>(_model);
                // Get prediction from main model
                var prediction = predictionEngine.Predict(data);
                var predictedValue = prediction.PredictedValue;

                // If shadow mode is enabled, get predictions from shadow models
                if (_shadowModeEnabled)
                {
                    foreach (var (modelId, shadowModel) in _shadowModels)
                    {
                        var features = new Dictionary<string, object>
                        {
                            { "Frequency", data.Frequency },
                            { "HourOfDay", data.HourOfDay },
                            { "DayOfWeek", data.DayOfWeek },
                            { "ResponseTime", data.ResponseTime },
                            { "CacheHitRate", data.CacheHitRate }
                        };

                        await _validationService.ComparePredictionAsync(modelId, key, predictedValue, features);
                    }

                    // Check if any shadow models should be promoted or rolled back
                    await CheckShadowModelsAsync();
                }

                _logger.LogDebug("ML prediction for key {Key}: {Value}", key, predictedValue);
                return predictedValue;
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

        public async Task UpdateModelAsync(string modelVersionId, bool useShadowMode = true)
        {
            try
            {
                var version = await _trainingService.GetModelVersion(modelVersionId);
                if (version == null)
                {
                    throw new InvalidOperationException($"Model version {modelVersionId} not found");
                }

                var newModel = await Task.Run(() => _mlContext.Model.Load(version.FilePath, out var _));

                // If shadow mode is enabled, validate the model first
                if (useShadowMode)
                {
                    var testData = await _trainingService.GetTestDataAsync();
                    var metrics = await _validationService.ValidateModelAsync(newModel, testData);

                    if (metrics.Accuracy < 0.9 || metrics.F1Score < 0.85)
                    {
                        _logger.LogWarning(
                            "Model version {Version} failed validation: Accuracy={Accuracy}, F1={F1}",
                            modelVersionId, metrics.Accuracy, metrics.F1Score);
                        throw new InvalidOperationException("Model failed validation checks");
                    }

                    // Start shadow deployment
                    await _validationService.StartShadowDeploymentAsync(modelVersionId, newModel);
                    _shadowModels[modelVersionId] = newModel;
                    _shadowModeEnabled = true;

                    _logger.LogInformation(
                        "Started shadow deployment for model version {Version}",
                        modelVersionId);
                }
                else
                {
                    // Direct deployment
                    _model = newModel;
                    _activeVersion = version;
                    _logger.LogInformation(
                        "Successfully loaded ML model version {Version}",
                        modelVersionId);
                }
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

        private async Task CheckShadowModelsAsync()
        {
            foreach (var (modelId, shadowModel) in _shadowModels.ToList())
            {
                // Check if shadow model should be promoted
                if (await _validationService.ShouldPromoteModelAsync(modelId))
                {
                    _logger.LogInformation("Promoting shadow model {ModelId} to production", modelId);
                    _model = shadowModel;
                    _shadowModels.Remove(modelId);
                    await _validationService.StopShadowDeploymentAsync(modelId);

                    // Update active version
                    var version = await _trainingService.GetModelVersion(modelId);
                    if (version != null)
                    {
                        _activeVersion = version;
                    }
                }
                // Check if shadow model should be rolled back
                else if (await _validationService.ShouldRollbackModelAsync(modelId))
                {
                    _logger.LogWarning("Rolling back shadow model {ModelId}", modelId);
                    _shadowModels.Remove(modelId);
                    await _validationService.StopShadowDeploymentAsync(modelId);
                }
            }

            // Disable shadow mode if no models are being tested
            if (!_shadowModels.Any())
            {
                _shadowModeEnabled = false;
            }
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

        private async Task<double> PredictUsingHeuristics(string key)
        {
            var parts = key.Split(':');
            var baseScore = 1.0;

            if (parts.Length > 1)
            {
                baseScore *= parts.Length;
            }

            var hourOfDay = DateTime.UtcNow.Hour;
            var timeWeight = (hourOfDay >= 8 && hourOfDay <= 18) ? 1.5 : 1.0;

            return baseScore * timeWeight;
        }

        private async Task<float> GetQueryFrequency(string key)
        {
            try
            {
                var frequency = await _metricsService.GetQueryFrequency(key, TimeSpan.FromHours(1));
                return (float)frequency;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting query frequency for key: {Key}", key);
                return 0.0f;
            }
        }

        private async Task<float> GetAverageResponseTime(string key)
        {
            try
            {
                var responseTime = await _metricsService.GetAverageResponseTime(key, TimeSpan.FromMinutes(5));
                return (float)responseTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting average response time for key: {Key}", key);
                return 0.0f;
            }
        }

        private async Task<float> GetCacheHitRate(string key)
        {
            try
            {
                var hitRate = await _metricsService.GetCacheHitRate(key, TimeSpan.FromMinutes(5));
                return (float)hitRate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache hit rate for key: {Key}", key);
                return 0.0f;
            }
        }

        public async Task CollectTrainingData(
            string key, 
            bool wasHit, 
            double responseTime, 
            double resourceUsage)
        {
            try
            {
                var data = new CacheTrainingData
                {
                    Key = key,
                    AccessFrequency = await GetQueryFrequency(key),
                    TimeOfDay = DateTime.UtcNow.Hour,
                    DayOfWeek = (float)DateTime.UtcNow.DayOfWeek,
                    UserActivityLevel = await CalculateUserActivityLevel(),
                    ResponseTime = (float)responseTime,
                    CacheHitRate = await GetCacheHitRate(key),
                    ResourceUsage = (float)resourceUsage,
                    PerformanceGain = wasHit ? (float)(1000.0 / responseTime) : 0f
                };

                await _trainingService.AddTrainingDataPoint(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting training data for key: {Key}", key);
            }
        }

        private async Task<float> CalculateUserActivityLevel()
        {
            try
            {
                var activityLevel = await _metricsService.GetUserActivityLevel(TimeSpan.FromMinutes(15));
                return (float)activityLevel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating user activity level");
                return 0.0f;
            }
        }
    }
}