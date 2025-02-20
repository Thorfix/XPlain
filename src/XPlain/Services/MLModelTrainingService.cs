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

        public async Task AddTrainingDataPoint(CacheTrainingData data)
        {
            _trainingData.Add(data);
            await CheckAndTriggerRetraining();
        }

        private readonly List<CacheTrainingData> _trainingData = new();
        private readonly int _retrainingThreshold = 1000; // Retrain after collecting 1000 new data points
        private readonly double _dataDriftThreshold = 0.1; // 10% drift threshold

        private async Task CheckAndTriggerRetraining()
        {
            if (_trainingData.Count >= _retrainingThreshold || await DetectDataDrift())
            {
                await TriggerRetraining();
            }
        }

        private async Task<bool> DetectDataDrift()
        {
            if (_activeVersion == null || _trainingData.Count < 100)
                return false;

            var currentDistribution = CalculateDistribution(_trainingData.TakeLast(100));
            var baselineDistribution = _activeVersion.Metadata.DatasetCharacteristics;

            return CalculateDistributionDrift(currentDistribution, baselineDistribution) > _dataDriftThreshold;
        }

        private Dictionary<string, double> CalculateDistribution(IEnumerable<CacheTrainingData> data)
        {
            return new Dictionary<string, double>
            {
                ["avgAccessFrequency"] = data.Average(d => d.AccessFrequency),
                ["avgResourceUsage"] = data.Average(d => d.ResourceUsage),
                ["avgHitRate"] = data.Average(d => d.CacheHitRate),
                ["avgResponseTime"] = data.Average(d => d.ResponseTime)
            };
        }

        private double CalculateDistributionDrift(Dictionary<string, double> current, Dictionary<string, string> baseline)
        {
            double totalDrift = 0;
            int metrics = 0;

            foreach (var kvp in current)
            {
                if (baseline.TryGetValue(kvp.Key, out var baselineValue) && 
                    double.TryParse(baselineValue, out var baselineDouble))
                {
                    totalDrift += Math.Abs((kvp.Value - baselineDouble) / baselineDouble);
                    metrics++;
                }
            }

            return metrics > 0 ? totalDrift / metrics : 0;
        }

        public async Task<ModelVersion> TrainModelAsync(List<CacheTrainingData> trainingData, ModelTrainingParameters parameters)
        {
            try
            {
                _logger.LogInformation("Starting model training with {Count} data points", trainingData.Count);

                // Split data into training and validation sets
                var dataSplit = _mlContext.Data.TrainTestSplit(
                    _mlContext.Data.LoadFromEnumerable(trainingData),
                    testFraction: 0.2);

                // Create feature engineering pipeline
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

                // Train the model on training data
                _trainedModel = trainingPipeline.Fit(dataSplit.TrainSet);

                // Evaluate the model on validation data
                var predictions = _trainedModel.Transform(dataSplit.TestSet);
                var metrics = _mlContext.Regression.Evaluate(predictions);

                // Perform cross-validation
                var cvResults = _mlContext.Regression.CrossValidate(
                    dataSplit.TrainSet, 
                    trainingPipeline, 
                    numberOfFolds: 5);

                var avgRSquared = cvResults.Average(r => r.Metrics.RSquared);
                var avgRMSE = cvResults.Average(r => r.Metrics.RootMeanSquaredError);

                _logger.LogInformation(
                    "Model trained successfully. Validation metrics - R²: {RSquared}, RMSE: {RMSE}. " +
                    "Cross-validation metrics - Avg R²: {AvgRSquared}, Avg RMSE: {AvgRMSE}",
                    metrics.RSquared,
                    metrics.RootMeanSquaredError,
                    avgRSquared,
                    avgRMSE);

                // Perform feature importance analysis
                var permutationMetrics = _mlContext.Regression
                    .PermutationFeatureImportance(_trainedModel, predictions);

                var featureImportance = new Dictionary<string, double>();
                var featureColumns = predictions.Schema
                    .Where(col => col.Name.EndsWith("Features"))
                    .Select(col => col.Name)
                    .ToList();

                foreach (var feature in featureColumns)
                {
                    featureImportance[feature] = Math.Abs(permutationMetrics
                        .Select(m => m.RSquared)
                        .Average());
                }

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

        public async Task<ModelVersion?> ActivateModelVersion(string versionId, bool performSafetyChecks = true)
        {
            var version = await GetModelVersion(versionId);
            if (version == null)
            {
                throw new InvalidOperationException($"Model version {versionId} not found");
            }

            if (performSafetyChecks)
            {
                var safetyChecksPassed = await PerformModelSafetyChecks(version);
                if (!safetyChecksPassed)
                {
                    throw new InvalidOperationException("Model safety checks failed - activation aborted");
                }
            }

            try
            {
                // Load the model first to ensure it's valid
                var newModel = await LoadModelAsync(version.FilePath);

                // Perform A/B testing if previous version exists
                if (_activeVersion != null)
                {
                    var comparisonResult = await CompareModelVersions(_activeVersion.Id, version.Id);
                    if (!comparisonResult.IsCandidateBetter)
                    {
                        _logger.LogWarning("New model version does not show significant improvement");
                        if (!performSafetyChecks) // Only throw if not in force mode
                        {
                            throw new InvalidOperationException("New model does not show significant improvement");
                        }
                    }

                    _activeVersion.IsActive = false;
                }

                version.IsActive = true;
                _activeVersion = version;
                _trainedModel = newModel;

                _logger.LogInformation("Successfully activated model version {VersionId}", versionId);
                return version;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating model version {VersionId}", versionId);
                throw;
            }
        }

        private async Task<bool> PerformModelSafetyChecks(ModelVersion version)
        {
            try
            {
                // Check 1: Validate model file integrity
                if (!File.Exists(version.FilePath))
                {
                    _logger.LogError("Model file not found: {FilePath}", version.FilePath);
                    return false;
                }

                // Check 2: Validate performance metrics
                var metrics = version.Metadata.ValidationMetrics;
                if (metrics["RSquared"] < 0.5 || metrics["RootMeanSquaredError"] > 0.3)
                {
                    _logger.LogError("Model metrics below acceptable thresholds");
                    return false;
                }

                // Check 3: Validate prediction stability
                if (!await ValidatePredictionStability(version))
                {
                    _logger.LogError("Model predictions show instability");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing model safety checks");
                return false;
            }
        }

        private async Task<bool> ValidatePredictionStability(ModelVersion version)
        {
            try
            {
                var model = await LoadModelAsync(version.FilePath);
                var testData = GenerateStabilityTestData();
                var predictionEngine = _mlContext.Model.CreatePredictionEngine<CacheTrainingData, CachePrediction>(model);

                var predictions = testData.Select(d => predictionEngine.Predict(d).PredictedValue).ToList();
                
                // Check for extreme predictions
                if (predictions.Any(p => p < 0 || p > 1000))
                    return false;

                // Check for prediction variance
                var variance = CalculateVariance(predictions);
                return variance < 100; // Threshold for acceptable variance
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating prediction stability");
                return false;
            }
        }

        private List<CacheTrainingData> GenerateStabilityTestData()
        {
            var testData = new List<CacheTrainingData>();
            var random = new Random(42); // Fixed seed for reproducibility

            for (int i = 0; i < 100; i++)
            {
                testData.Add(new CacheTrainingData
                {
                    Key = $"test_key_{i}",
                    AccessFrequency = (float)random.NextDouble(),
                    TimeOfDay = random.Next(0, 24),
                    DayOfWeek = random.Next(0, 7),
                    UserActivityLevel = (float)random.NextDouble(),
                    ResponseTime = (float)(random.NextDouble() * 1000),
                    CacheHitRate = (float)random.NextDouble(),
                    ResourceUsage = (float)random.NextDouble(),
                    PerformanceGain = (float)random.NextDouble()
                });
            }

            return testData;
        }

        private double CalculateVariance(List<float> values)
        {
            var mean = values.Average();
            return values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
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
                try
                {
                    if (File.Exists(version.FilePath))
                    {
                        File.Delete(version.FilePath);
                        _logger.LogInformation("Deleted old model file: {FilePath}", version.FilePath);
                    }
                    _modelVersions.Remove(version);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting old model: {VersionId}", version.Id);
                }
            }
        }

        public async Task<ModelHealthReport> GenerateModelHealthReport()
        {
            if (_activeVersion == null)
            {
                return new ModelHealthReport
                {
                    Status = "No active model",
                    IsHealthy = false
                };
            }

            try
            {
                var recentPredictions = await GetRecentPredictionMetrics();
                var dataDrift = await DetectDataDrift();
                var modelAge = DateTime.UtcNow - _activeVersion.CreatedAt;

                return new ModelHealthReport
                {
                    ModelId = _activeVersion.Id,
                    Status = "Active",
                    IsHealthy = recentPredictions.Accuracy > 0.8 && !dataDrift,
                    Metrics = new Dictionary<string, double>
                    {
                        ["prediction_accuracy"] = recentPredictions.Accuracy,
                        ["prediction_latency_ms"] = recentPredictions.AverageLatency,
                        ["data_drift_score"] = await CalculateDataDriftScore(),
                        ["model_age_days"] = modelAge.TotalDays
                    },
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating model health report");
                return new ModelHealthReport
                {
                    Status = "Error",
                    IsHealthy = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}