using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace XPlain.Services
{
    public class MLValidationMetrics
    {
        public double Accuracy { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double F1Score { get; set; }
        public double RSquared { get; set; }
        public double MeanAbsoluteError { get; set; }
        public double RootMeanSquaredError { get; set; }
        public DateTime ValidatedAt { get; set; }
    }

    public class ShadowModelResults
    {
        public string ModelId { get; set; }
        public Dictionary<string, double> Predictions { get; set; }
        public double AverageError { get; set; }
        public int TotalPredictions { get; set; }
        public int SuccessfulPredictions { get; set; }
        public DateTime StartedAt { get; set; }
        public List<string> Warnings { get; set; }
    }

    public class MLModelValidationService
    {
        private readonly MLContext _mlContext;
        private readonly ILogger<MLModelValidationService> _logger;
        private readonly MLModelTrainingService _trainingService;
        private readonly Dictionary<string, ShadowModelResults> _shadowResults;
        private readonly Dictionary<string, ITransformer> _shadowModels;
        private readonly double _promotionThreshold = 0.95; // 95% accuracy required for promotion
        private readonly double _rollbackThreshold = 0.85; // 85% accuracy triggers rollback
        private readonly TimeSpan _minimumValidationPeriod = TimeSpan.FromHours(1);
        private readonly TimeSpan _maximumShadowPeriod = TimeSpan.FromDays(1);

        public MLModelValidationService(
            ILogger<MLModelValidationService> logger,
            MLModelTrainingService trainingService)
        {
            _mlContext = new MLContext(seed: 1);
            _logger = logger;
            _trainingService = trainingService;
            _shadowResults = new Dictionary<string, ShadowModelResults>();
            _shadowModels = new Dictionary<string, ITransformer>();
        }

        public async Task<MLValidationMetrics> ValidateModelAsync(ITransformer model, IEnumerable<CacheTrainingData> testData)
        {
            try
            {
                var metrics = new MLValidationMetrics { ValidatedAt = DateTime.UtcNow };
                
                // Convert test data to IDataView
                var testDataView = _mlContext.Data.LoadFromEnumerable(testData);
                
                // Calculate regression metrics
                var predictions = model.Transform(testDataView);
                var regressionMetrics = _mlContext.Regression.Evaluate(predictions);

                metrics.RSquared = regressionMetrics.RSquared;
                metrics.MeanAbsoluteError = regressionMetrics.MeanAbsoluteError;
                metrics.RootMeanSquaredError = regressionMetrics.RootMeanSquaredError;

                // Calculate classification metrics for binary decisions
                metrics.Accuracy = CalculateAccuracy(predictions, testData);
                metrics.Precision = CalculatePrecision(predictions, testData);
                metrics.Recall = CalculateRecall(predictions, testData);
                metrics.F1Score = 2 * (metrics.Precision * metrics.Recall) / (metrics.Precision + metrics.Recall);

                _logger.LogInformation("Model validation metrics calculated: Accuracy={Accuracy}, F1={F1}", 
                    metrics.Accuracy, metrics.F1Score);

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating ML model");
                throw;
            }
        }

        public async Task<bool> StartShadowDeploymentAsync(string modelId, ITransformer model)
        {
            try
            {
                if (_shadowModels.ContainsKey(modelId))
                {
                    _logger.LogWarning("Shadow deployment already exists for model {ModelId}", modelId);
                    return false;
                }

                _shadowModels[modelId] = model;
                _shadowResults[modelId] = new ShadowModelResults
                {
                    ModelId = modelId,
                    Predictions = new Dictionary<string, double>(),
                    StartedAt = DateTime.UtcNow,
                    TotalPredictions = 0,
                    SuccessfulPredictions = 0,
                    Warnings = new List<string>()
                };

                _logger.LogInformation("Started shadow deployment for model {ModelId}", modelId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting shadow deployment for model {ModelId}", modelId);
                return false;
            }
        }

        public async Task<double> ComparePredictionAsync(
            string modelId, 
            string key, 
            double actualValue,
            Dictionary<string, object> features)
        {
            if (!_shadowModels.ContainsKey(modelId))
            {
                return 0;
            }

            try
            {
                var shadowModel = _shadowModels[modelId];
                var results = _shadowResults[modelId];

                // Create prediction engine
                var predictionEngine = _mlContext.Model.CreatePredictionEngine<QueryPredictionData, QueryPrediction>(shadowModel);

                // Convert features to prediction data
                var data = new QueryPredictionData
                {
                    QueryKey = key,
                    Frequency = Convert.ToSingle(features["Frequency"]),
                    HourOfDay = Convert.ToSingle(features["HourOfDay"]),
                    DayOfWeek = Convert.ToSingle(features["DayOfWeek"]),
                    ResponseTime = Convert.ToSingle(features["ResponseTime"]),
                    CacheHitRate = Convert.ToSingle(features["CacheHitRate"])
                };

                // Make prediction
                var prediction = predictionEngine.Predict(data);
                var error = Math.Abs(prediction.PredictedValue - actualValue);

                // Update results
                results.TotalPredictions++;
                if (error <= actualValue * 0.1) // Within 10% error margin
                {
                    results.SuccessfulPredictions++;
                }

                results.Predictions[key] = prediction.PredictedValue;
                results.AverageError = results.Predictions.Values.Average(p => Math.Abs(p - actualValue));

                // Check for warnings
                if (error > actualValue * 0.2) // More than 20% error
                {
                    results.Warnings.Add($"High prediction error for key {key}: expected {actualValue}, got {prediction.PredictedValue}");
                }

                return prediction.PredictedValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing prediction for model {ModelId}", modelId);
                return 0;
            }
        }

        public async Task<bool> ShouldPromoteModelAsync(string modelId)
        {
            if (!_shadowResults.ContainsKey(modelId))
            {
                return false;
            }

            var results = _shadowResults[modelId];
            var runningTime = DateTime.UtcNow - results.StartedAt;

            // Ensure minimum validation period
            if (runningTime < _minimumValidationPeriod)
            {
                return false;
            }

            // Check if we have enough predictions
            if (results.TotalPredictions < 1000)
            {
                return false;
            }

            // Calculate accuracy
            var accuracy = (double)results.SuccessfulPredictions / results.TotalPredictions;

            // Check if accuracy meets promotion threshold
            return accuracy >= _promotionThreshold && results.Warnings.Count < 10;
        }

        public async Task<bool> ShouldRollbackModelAsync(string modelId)
        {
            if (!_shadowResults.ContainsKey(modelId))
            {
                return false;
            }

            var results = _shadowResults[modelId];
            var runningTime = DateTime.UtcNow - results.StartedAt;

            // Check if maximum shadow period exceeded
            if (runningTime > _maximumShadowPeriod)
            {
                _logger.LogWarning("Shadow deployment period exceeded for model {ModelId}", modelId);
                return true;
            }

            // Calculate accuracy
            var accuracy = (double)results.SuccessfulPredictions / results.TotalPredictions;

            // Check if accuracy is below rollback threshold
            if (accuracy < _rollbackThreshold && results.TotalPredictions > 100)
            {
                _logger.LogWarning("Model {ModelId} accuracy {Accuracy} below rollback threshold", modelId, accuracy);
                return true;
            }

            // Check for excessive warnings
            if (results.Warnings.Count > 50)
            {
                _logger.LogWarning("Model {ModelId} has excessive warnings ({Count})", modelId, results.Warnings.Count);
                return true;
            }

            return false;
        }

        public async Task StopShadowDeploymentAsync(string modelId)
        {
            if (_shadowModels.ContainsKey(modelId))
            {
                _shadowModels.Remove(modelId);
                _shadowResults.Remove(modelId);
                _logger.LogInformation("Stopped shadow deployment for model {ModelId}", modelId);
            }
        }

        public async Task<Dictionary<string, ValidationStatistics>> GetValidationStatisticsAsync()
        {
            var statistics = new Dictionary<string, ValidationStatistics>();

            foreach (var (modelId, results) in _shadowResults)
            {
                statistics[modelId] = new ValidationStatistics
                {
                    TotalPredictions = results.TotalPredictions,
                    SuccessfulPredictions = results.SuccessfulPredictions,
                    AverageError = results.AverageError,
                    RunningTime = DateTime.UtcNow - results.StartedAt,
                    WarningCount = results.Warnings.Count,
                    RecentWarnings = results.Warnings.TakeLast(10).ToList()
                };
            }

            return statistics;
        }

        private double CalculateAccuracy(IDataView predictions, IEnumerable<CacheTrainingData> testData)
        {
            var predictionColumn = predictions.GetColumn<float>("PredictedValue").ToArray();
            var actualValues = testData.Select(d => d.PerformanceGain).ToArray();

            int correctPredictions = 0;
            for (int i = 0; i < predictionColumn.Length; i++)
            {
                // Consider prediction correct if within 10% of actual value
                if (Math.Abs(predictionColumn[i] - actualValues[i]) <= actualValues[i] * 0.1)
                {
                    correctPredictions++;
                }
            }

            return (double)correctPredictions / predictionColumn.Length;
        }

        private double CalculatePrecision(IDataView predictions, IEnumerable<CacheTrainingData> testData)
        {
            var predictionColumn = predictions.GetColumn<float>("PredictedValue").ToArray();
            var actualValues = testData.Select(d => d.PerformanceGain).ToArray();

            int truePositives = 0;
            int falsePositives = 0;

            for (int i = 0; i < predictionColumn.Length; i++)
            {
                if (predictionColumn[i] > 0)
                {
                    if (actualValues[i] > 0)
                    {
                        truePositives++;
                    }
                    else
                    {
                        falsePositives++;
                    }
                }
            }

            return truePositives + falsePositives == 0 ? 0 : (double)truePositives / (truePositives + falsePositives);
        }

        private double CalculateRecall(IDataView predictions, IEnumerable<CacheTrainingData> testData)
        {
            var predictionColumn = predictions.GetColumn<float>("PredictedValue").ToArray();
            var actualValues = testData.Select(d => d.PerformanceGain).ToArray();

            int truePositives = 0;
            int falseNegatives = 0;

            for (int i = 0; i < predictionColumn.Length; i++)
            {
                if (actualValues[i] > 0)
                {
                    if (predictionColumn[i] > 0)
                    {
                        truePositives++;
                    }
                    else
                    {
                        falseNegatives++;
                    }
                }
            }

            return truePositives + falseNegatives == 0 ? 0 : (double)truePositives / (truePositives + falseNegatives);
        }
    }

    public class ValidationStatistics
    {
        public int TotalPredictions { get; set; }
        public int SuccessfulPredictions { get; set; }
        public double AverageError { get; set; }
        public TimeSpan RunningTime { get; set; }
        public int WarningCount { get; set; }
        public List<string> RecentWarnings { get; set; }
    }

    public class QueryPredictionData
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

    public class QueryPrediction
    {
        [ColumnName("PredictedValue")]
        public float PredictedValue { get; set; }
    }
}