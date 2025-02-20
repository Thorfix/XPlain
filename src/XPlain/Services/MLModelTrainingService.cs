using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class ModelTrainingParameters
    {
        public bool IncludeTimeFeatures { get; set; }
        public bool IncludeUserActivityPatterns { get; set; }
        public bool IncludeQueryPatterns { get; set; }
        public bool OptimizeForResourceUsage { get; set; }
        public Dictionary<string, object> Features { get; set; } = new();
        public Dictionary<string, double> Weights { get; set; } = new();
    }

    public class CacheTrainingData
    {
        [LoadColumn(0)]
        public string Key { get; set; } = "";

        [LoadColumn(1)]
        public float AccessFrequency { get; set; }

        [LoadColumn(2)]
        public float TimeOfDay { get; set; }

        [LoadColumn(3)]
        public float DayOfWeek { get; set; }

        [LoadColumn(4)]
        public float UserActivityLevel { get; set; }

        [LoadColumn(5)]
        public float ResponseTime { get; set; }

        [LoadColumn(6)]
        public float CacheHitRate { get; set; }

        [LoadColumn(7)]
        public float ResourceUsage { get; set; }

        [LoadColumn(8)]
        public float PerformanceGain { get; set; }

        [LoadColumn(9)]
        public float Label { get; set; } // Predicted cache value
    }

    public class MLModelTrainingService
    {
        private readonly MLContext _mlContext;
        private readonly ILogger<MLModelTrainingService> _logger;
        private ITransformer? _trainedModel;

        public MLModelTrainingService(ILogger<MLModelTrainingService> logger)
        {
            _mlContext = new MLContext(seed: 1);
            _logger = logger;
        }

        public async Task TrainModelAsync(List<CacheTrainingData> trainingData, ModelTrainingParameters parameters)
        {
            try
            {
                var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

                // Create data processing pipeline
                var pipeline = _mlContext.Transforms
                    .Concatenate("Features",
                        "AccessFrequency",
                        "ResponseTime",
                        "CacheHitRate",
                        "ResourceUsage",
                        "PerformanceGain");

                // Add time-based features if requested
                if (parameters.IncludeTimeFeatures)
                {
                    pipeline = pipeline.Append(_mlContext.Transforms.Concatenate(
                        "TimeFeatures",
                        "TimeOfDay",
                        "DayOfWeek"));
                }

                // Add user activity features if requested
                if (parameters.IncludeUserActivityPatterns)
                {
                    pipeline = pipeline.Append(_mlContext.Transforms.Concatenate(
                        "ActivityFeatures",
                        "UserActivityLevel"));
                }

                // Normalize features
                pipeline = pipeline
                    .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                    .Append(_mlContext.Transforms.NormalizeMinMax("TimeFeatures"))
                    .Append(_mlContext.Transforms.NormalizeMinMax("ActivityFeatures"));

                // Configure the trainer
                var trainerOptions = new LbfgsMaximumEntropyMulticlassTrainer.Options
                {
                    L1Regularization = 0.01f,
                    L2Regularization = 0.01f,
                    OptimizationTolerance = 1e-7f,
                    HistorySize = 50,
                    MaximumNumberOfIterations = 100
                };

                // Add the trainer to the pipeline
                var trainingPipeline = pipeline.Append(
                    _mlContext.Regression.Trainers.LbfgsPoissonRegression(
                        labelColumnName: "Label",
                        featureColumnName: "Features")
                );

                // Train the model
                _trainedModel = trainingPipeline.Fit(dataView);

                // Evaluate the model
                var predictions = _trainedModel.Transform(dataView);
                var metrics = _mlContext.Regression.Evaluate(predictions);

                _logger.LogInformation(
                    "Model trained successfully. R² score: {RSquared}, RMS error: {RootMeanSquaredError}",
                    metrics.RSquared,
                    metrics.RootMeanSquaredError);

                // Save the model
                await Task.Run(() => _mlContext.Model.Save(_trainedModel, dataView.Schema, "cache_prediction_model.zip"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error training cache prediction model");
                throw;
            }
        }

        public async Task<Dictionary<string, double>> PredictCacheValuesAsync(List<CacheTrainingData> data)
        {
            if (_trainedModel == null)
            {
                throw new InvalidOperationException("Model has not been trained");
            }

            var predictionEngine = _mlContext.Model.CreatePredictionEngine<CacheTrainingData, CachePrediction>(_trainedModel);
            var results = new Dictionary<string, double>();

            foreach (var item in data)
            {
                var prediction = predictionEngine.Predict(item);
                results[item.Key] = prediction.PredictedValue;
            }

            return results;
        }

        public class CachePrediction
        {
            [ColumnName("Score")]
            public float PredictedValue { get; set; }
        }

        public async Task<List<string>> GetActiveFeatures()
        {
            if (_trainedModel == null)
            {
                return new List<string>();
            }

            return _trainedModel.GetOutputSchema(GetCurrentSchema())
                .Select(col => col.Name)
                .Where(name => name.EndsWith("Features"))
                .ToList();
        }

        private DataViewSchema GetCurrentSchema()
        {
            return _mlContext.Data.LoadFromEnumerable(new List<CacheTrainingData>()).Schema;
        }
    }
}