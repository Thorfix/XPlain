using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
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
        private readonly string _modelsDirectory = "ml_models";
        private readonly List<ModelVersion> _modelVersions = new();
        private ModelVersion? _activeVersion;

        public MLModelTrainingService(ILogger<MLModelTrainingService> logger)
        {
            _mlContext = new MLContext(seed: 1);
            _logger = logger;
        }

        public async Task<ModelVersion> TrainModelAsync(List<CacheTrainingData> trainingData, ModelTrainingParameters parameters)
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

                // Create model version and metadata
                var modelVersion = new ModelVersion
                {
                    ModelName = "cache_prediction_model",
                    CreatedAt = DateTime.UtcNow,
                    TrainingParameters = parameters.Features,
                    Metadata = new ModelMetadata
                    {
                        DatasetSize = trainingData.Count,
                        TrainingStartTime = DateTime.UtcNow,
                        Features = parameters.Features.Keys.ToList(),
                        DatasetCharacteristics = new Dictionary<string, string>
                        {
                            ["avgAccessFrequency"] = trainingData.Average(d => d.AccessFrequency).ToString("F2"),
                            ["avgResourceUsage"] = trainingData.Average(d => d.ResourceUsage).ToString("F2"),
                            ["avgHitRate"] = trainingData.Average(d => d.CacheHitRate).ToString("F2")
                        }
                    }
                };

                // Create models directory if it doesn't exist
                Directory.CreateDirectory(_modelsDirectory);

                // Save the model with version information
                string modelPath = Path.Combine(_modelsDirectory, $"{modelVersion.Id}.zip");
                await Task.Run(() => _mlContext.Model.Save(_trainedModel, dataView.Schema, modelPath));

                // Update metadata and save metrics
                modelVersion.FilePath = modelPath;
                modelVersion.Metadata.TrainingEndTime = DateTime.UtcNow;
                modelVersion.Metadata.ValidationMetrics = new Dictionary<string, double>
                {
                    ["RSquared"] = metrics.RSquared,
                    ["RootMeanSquaredError"] = metrics.RootMeanSquaredError,
                    ["MeanAbsoluteError"] = metrics.MeanAbsoluteError
                };
                modelVersion.Status = "trained";
                modelVersion.IsActive = true;

                // If there was a previous active version, deactivate it
                if (_activeVersion != null)
                {
                    _activeVersion.IsActive = false;
                }

                _activeVersion = modelVersion;
                _modelVersions.Add(modelVersion);

                _logger.LogInformation(
                    "Model version {VersionId} trained and saved successfully",
                    modelVersion.Id);

                return modelVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error training cache prediction model");
                throw;
            }
        }

        public async Task<Dictionary<string, double>> PredictCacheValuesAsync(List<CacheTrainingData> data, string? modelVersionId = null)
        {
            ITransformer model;
            if (modelVersionId != null)
            {
                var version = _modelVersions.FirstOrDefault(v => v.Id == modelVersionId);
                if (version == null)
                {
                    throw new InvalidOperationException($"Model version {modelVersionId} not found");
                }
                model = await LoadModelAsync(version.FilePath);
            }
            else if (_trainedModel != null)
            {
                model = _trainedModel;
            }
            else
            {
                throw new InvalidOperationException("No model available for predictions");
            }

            var predictionEngine = _mlContext.Model.CreatePredictionEngine<CacheTrainingData, CachePrediction>(model);
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

        private async Task<ITransformer> LoadModelAsync(string modelPath)
        {
            return await Task.Run(() => _mlContext.Model.Load(modelPath, out var _));
        }

        public async Task<ModelVersion?> GetActiveModelVersion()
        {
            return _activeVersion;
        }

        public async Task<List<ModelVersion>> GetModelVersions()
        {
            return _modelVersions.OrderByDescending(v => v.CreatedAt).ToList();
        }

        public async Task<ModelVersion?> GetModelVersion(string versionId)
        {
            return _modelVersions.FirstOrDefault(v => v.Id == versionId);
        }

        public async Task<ModelComparisonResult> CompareModelVersions(string baselineVersionId, string candidateVersionId)
        {
            var baseline = await GetModelVersion(baselineVersionId);
            var candidate = await GetModelVersion(candidateVersionId);

            if (baseline == null || candidate == null)
            {
                throw new InvalidOperationException("One or both model versions not found");
            }

            var result = new ModelComparisonResult
            {
                BaselineModelId = baselineVersionId,
                CandidateModelId = candidateVersionId,
                MetricDifferences = new Dictionary<string, double>()
            };

            foreach (var metric in baseline.Metadata.ValidationMetrics.Keys)
            {
                if (candidate.Metadata.ValidationMetrics.ContainsKey(metric))
                {
                    result.MetricDifferences[metric] = 
                        candidate.Metadata.ValidationMetrics[metric] - baseline.Metadata.ValidationMetrics[metric];
                }
            }

            // Determine if candidate is better
            var rSquaredImprovement = result.MetricDifferences.GetValueOrDefault("RSquared", 0);
            var rmseImprovement = -result.MetricDifferences.GetValueOrDefault("RootMeanSquaredError", 0);
            
            result.IsCandidateBetter = rSquaredImprovement > 0.01 || rmseImprovement > 0.01;
            result.RecommendedAction = result.IsCandidateBetter ? "promote" : "keep_baseline";

            return result;
        }

        public async Task<ModelVersion?> ActivateModelVersion(string versionId)
        {
            var version = await GetModelVersion(versionId);
            if (version == null)
            {
                throw new InvalidOperationException($"Model version {versionId} not found");
            }

            if (_activeVersion != null)
            {
                _activeVersion.IsActive = false;
            }

            version.IsActive = true;
            _activeVersion = version;
            _trainedModel = await LoadModelAsync(version.FilePath);

            return version;
        }

        public async Task CleanupOldModels(int keepVersions = 5)
        {
            var versionsToDelete = _modelVersions
                .Where(v => !v.IsActive)
                .OrderByDescending(v => v.CreatedAt)
                .Skip(keepVersions)
                .ToList();

            foreach (var version in versionsToDelete)
            {
                if (File.Exists(version.FilePath))
                {
                    File.Delete(version.FilePath);
                }
                _modelVersions.Remove(version);
            }
        }
    }
}