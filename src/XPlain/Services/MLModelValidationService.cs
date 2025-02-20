using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace XPlain.Services
{
    public class ValidationMetrics
    {
        public double Accuracy { get; set; }
        public double F1Score { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
    }

    public class TestData
    {
        public Dictionary<string, object> Features { get; set; } = new();
        public float ExpectedValue { get; set; }
        public PredictionKind PredictionKind { get; set; }
    }

    public enum PredictionKind
    {
        BinaryClassification,
        MulticlassClassification,
        Regression
    }

    public class MLModelValidationService
    {
        private readonly ILogger<MLModelValidationService> _logger;
        private readonly MetricsCollectionService _metricsService;
        private readonly Dictionary<string, ModelValidationState> _shadowDeployments;
        private readonly Dictionary<string, ABTestState> _abTests;
        private readonly IAlertManagementService _alertService;
        private readonly TimeSeriesMetricsStore _metricsStore;

        public MLModelValidationService(
            ILogger<MLModelValidationService> logger,
            MetricsCollectionService metricsService,
            IAlertManagementService alertService,
            TimeSeriesMetricsStore metricsStore)
        {
            _logger = logger;
            _metricsService = metricsService;
            _alertService = alertService;
            _metricsStore = metricsStore;
            _shadowDeployments = new Dictionary<string, ModelValidationState>();
            _abTests = new Dictionary<string, ABTestState>();
        }

        public async Task<string> StartABTestAsync(string modelAId, string modelBId, ABTestConfig config)
        {
            var testId = Guid.NewGuid().ToString();
            _abTests[testId] = new ABTestState
            {
                ModelAId = modelAId,
                ModelBId = modelBId,
                Config = config,
                StartTime = DateTime.UtcNow,
                ResultsA = new List<PredictionResult>(),
                ResultsB = new List<PredictionResult>()
            };

            await _alertService.CreateAlertAsync(new Alert
            {
                Title = "A/B Test Started",
                Description = $"Started A/B test comparing models {modelAId} and {modelBId}",
                Severity = AlertSeverity.Info,
                Source = "ModelValidation"
            });

            return testId;
        }

        public async Task<ABTestResults> GetABTestResultsAsync(string testId)
        {
            if (!_abTests.TryGetValue(testId, out var state))
            {
                throw new KeyNotFoundException($"A/B test {testId} not found");
            }

            var statsA = CalculateModelStats(state.ResultsA);
            var statsB = CalculateModelStats(state.ResultsB);
            var significanceTest = PerformStatisticalTest(statsA, statsB);

            await _metricsStore.StoreMetrics($"abtest_{testId}", new
            {
                ModelA = statsA,
                ModelB = statsB,
                Significance = significanceTest
            });

            return new ABTestResults
            {
                TestId = testId,
                ModelAStats = statsA,
                ModelBStats = statsB,
                SignificanceTest = significanceTest,
                Winner = DetermineWinner(statsA, statsB, significanceTest),
                Duration = DateTime.UtcNow - state.StartTime,
                SampleSizeA = state.ResultsA.Count,
                SampleSizeB = state.ResultsB.Count
            };
        }

        private ModelStats CalculateModelStats(List<PredictionResult> results)
        {
            if (results.Count == 0)
                return new ModelStats();

            var accuracies = results.Select(r => Math.Abs(r.PredictedValue - r.ActualValue) < 0.1 ? 1.0 : 0.0).ToList();
            var latencies = results.Select(r => r.Latency).ToList();

            return new ModelStats
            {
                Accuracy = accuracies.Average(),
                StandardError = CalculateStandardError(accuracies),
                MeanLatency = latencies.Average(),
                LatencyP95 = CalculatePercentile(latencies, 0.95),
                SampleSize = results.Count
            };
        }

        private SignificanceTestResult PerformStatisticalTest(ModelStats statsA, ModelStats statsB)
        {
            // Perform two-sample z-test for proportions
            double pooledStdErr = Math.Sqrt(
                Math.Pow(statsA.StandardError, 2) + Math.Pow(statsB.StandardError, 2));

            double zScore = (statsA.Accuracy - statsB.Accuracy) / pooledStdErr;
            double pValue = 2 * (1 - NormalCDF(Math.Abs(zScore))); // Two-tailed test

            return new SignificanceTestResult
            {
                TestType = "Two-Sample Z-Test",
                ZScore = zScore,
                PValue = pValue,
                IsSignificant = pValue < 0.05
            };
        }

        private double NormalCDF(double x)
        {
            return 0.5 * (1 + Erf(x / Math.Sqrt(2)));
        }

        private double Erf(double x)
        {
            // Implementation of error function approximation
            double t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
            double tau = t * Math.Exp(-x * x - 1.26551223 +
                                    t * (1.00002368 +
                                    t * (0.37409196 +
                                    t * (0.09678418 +
                                    t * (-0.18628806 +
                                    t * (0.27886807 +
                                    t * (-1.13520398 +
                                    t * (1.48851587 +
                                    t * (-0.82215223 +
                                    t * 0.17087277)))))))));
            return x >= 0 ? 1 - tau : tau - 1;
        }

        private string DetermineWinner(ModelStats statsA, ModelStats statsB, SignificanceTestResult sigTest)
        {
            if (!sigTest.IsSignificant)
                return "No significant difference";

            return statsA.Accuracy > statsB.Accuracy ? "ModelA" : "ModelB";
        }

        private double CalculateStandardError(List<double> values)
        {
            double mean = values.Average();
            double variance = values.Sum(x => Math.Pow(x - mean, 2)) / (values.Count - 1);
            return Math.Sqrt(variance / values.Count);
        }

        private double CalculatePercentile(List<double> values, double percentile)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Max(0, index)];
        }

        public async Task<ValidationMetrics> ValidateModelAsync(
            ITransformer model,
            TestData testData)
        {
            try
            {
                // Get real metrics for validation
                var realMetrics = await GetRealMetricsAsync(testData.Features);
                testData = EnrichTestData(testData, realMetrics);
                
                return EvaluateModelMetrics(model, testData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during model validation");
                throw;
            }
        }

        private async Task<Dictionary<string, object>> GetRealMetricsAsync(Dictionary<string, object> features)
        {
            var realMetrics = new Dictionary<string, object>();
            var key = features.GetValueOrDefault("QueryKey")?.ToString() ?? "";

            // Get real metrics for the key
            var frequency = await _metricsService.GetQueryFrequency(key);
            var responseTime = await _metricsService.GetAverageResponseTime(key);
            var hitRate = await _metricsService.GetCacheHitRate(key);
            var activityLevel = await _metricsService.GetUserActivityLevel();

            realMetrics["Frequency"] = frequency;
            realMetrics["ResponseTime"] = responseTime;
            realMetrics["CacheHitRate"] = hitRate;
            realMetrics["UserActivityLevel"] = activityLevel;

            return realMetrics;
        }

        private TestData EnrichTestData(TestData testData, Dictionary<string, object> realMetrics)
        {
            // Enrich test data with real metrics for more accurate validation
            foreach (var (key, value) in realMetrics)
            {
                if (testData.Features.ContainsKey(key))
                {
                    testData.Features[key] = value;
                }
            }
            return testData;
        }

        private ValidationMetrics EvaluateModelMetrics(ITransformer model, TestData testData)
        {
            // Compute validation metrics using the enriched test data
            var metrics = new ValidationMetrics
            {
                Accuracy = CalculateAccuracy(model, testData),
                F1Score = CalculateF1Score(model, testData),
                Precision = CalculatePrecision(model, testData),
                Recall = CalculateRecall(model, testData)
            };

            return metrics;
        }

        public async Task StartShadowDeploymentAsync(string modelId, ITransformer model)
        {
            _shadowDeployments[modelId] = new ModelValidationState
            {
                Model = model,
                StartTime = DateTime.UtcNow,
                Predictions = new List<PredictionResult>()
            };
        }

        public async Task StopShadowDeploymentAsync(string modelId)
        {
            _shadowDeployments.Remove(modelId);
        }

        public async Task ComparePredictionAsync(
            string modelId,
            string key,
            double actualValue,
            Dictionary<string, object> features)
        {
            if (!_shadowDeployments.TryGetValue(modelId, out var state))
            {
                return;
            }

            try
            {
                // Add real metrics to features
                var realMetrics = await GetRealMetricsAsync(features);
                foreach (var (metricKey, value) in realMetrics)
                {
                    features[metricKey] = value;
                }

                // Record prediction result
                state.Predictions.Add(new PredictionResult
                {
                    Timestamp = DateTime.UtcNow,
                    Key = key,
                    ActualValue = actualValue,
                    Features = features
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing prediction for model {ModelId}", modelId);
            }
        }

        public async Task<bool> ShouldPromoteModelAsync(string modelId)
        {
            if (!_shadowDeployments.TryGetValue(modelId, out var state))
            {
                return false;
            }

            try
            {
                var deploymentTime = DateTime.UtcNow - state.StartTime;
                if (deploymentTime < TimeSpan.FromHours(1))
                {
                    return false; // Minimum deployment time not met
                }

                var predictions = state.Predictions;
                if (predictions.Count < 100)
                {
                    return false; // Not enough predictions to evaluate
                }

                // Calculate metrics using real data
                var accuracy = predictions.Count(p => Math.Abs(p.PredictedValue - p.ActualValue) < 0.1) / 
                             (double)predictions.Count;

                return accuracy > 0.9; // Promote if accuracy is above 90%
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating model {ModelId} for promotion", modelId);
                return false;
            }
        }

        public async Task<bool> ShouldRollbackModelAsync(string modelId)
        {
            if (!_shadowDeployments.TryGetValue(modelId, out var state))
            {
                return false;
            }

            try
            {
                var predictions = state.Predictions;
                if (predictions.Count < 10)
                {
                    return false; // Not enough predictions to evaluate
                }

                // Calculate error rate using real data
                var errorRate = predictions.Count(p => Math.Abs(p.PredictedValue - p.ActualValue) > 0.5) / 
                              (double)predictions.Count;

                return errorRate > 0.2; // Rollback if error rate is above 20%
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating model {ModelId} for rollback", modelId);
                return false;
            }
        }

        /// <summary>
        /// Calculates the accuracy metric for the model using ML.NET evaluation APIs.
        /// For binary and multiclass classification, this represents the ratio of correct predictions.
        /// For regression, this represents R-squared score.
        /// </summary>
        private double CalculateAccuracy(ITransformer model, TestData testData)
        {
            try
            {
                var mlContext = new MLContext();
                
                // Create IDataView from test data
                var dataView = CreateDataView(mlContext, testData);
                
                // Transform data using the model
                var transformedData = model.Transform(dataView);
                
                switch (testData.PredictionKind)
                {
                    case PredictionKind.BinaryClassification:
                        var binaryMetrics = mlContext.BinaryClassification.Evaluate(transformedData);
                        return binaryMetrics.Accuracy;
                        
                    case PredictionKind.MulticlassClassification:
                        var multiMetrics = mlContext.MulticlassClassification.Evaluate(transformedData);
                        return multiMetrics.MicroAccuracy;
                        
                    case PredictionKind.Regression:
                        var regMetrics = mlContext.Regression.Evaluate(transformedData);
                        return regMetrics.RSquared;
                        
                    default:
                        throw new ArgumentException($"Unsupported prediction kind: {testData.PredictionKind}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating accuracy metric");
                throw new InvalidOperationException("Failed to calculate accuracy metric", ex);
            }
        }

        /// <summary>
        /// Calculates the F1 score, which is the harmonic mean of precision and recall.
        /// Only applicable for classification tasks.
        /// </summary>
        private double CalculateF1Score(ITransformer model, TestData testData)
        {
            try
            {
                if (testData.PredictionKind == PredictionKind.Regression)
                {
                    throw new ArgumentException("F1 Score is not applicable for regression tasks");
                }

                var mlContext = new MLContext();
                var dataView = CreateDataView(mlContext, testData);
                var transformedData = model.Transform(dataView);

                if (testData.PredictionKind == PredictionKind.BinaryClassification)
                {
                    var metrics = mlContext.BinaryClassification.Evaluate(transformedData);
                    return metrics.F1Score;
                }
                else
                {
                    var metrics = mlContext.MulticlassClassification.Evaluate(transformedData);
                    return metrics.MacroF1Score;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating F1 score metric");
                throw new InvalidOperationException("Failed to calculate F1 score metric", ex);
            }
        }

        /// <summary>
        /// Calculates precision, which is the ratio of true positives to total predicted positives.
        /// Only applicable for classification tasks.
        /// </summary>
        private double CalculatePrecision(ITransformer model, TestData testData)
        {
            try
            {
                if (testData.PredictionKind == PredictionKind.Regression)
                {
                    throw new ArgumentException("Precision is not applicable for regression tasks");
                }

                var mlContext = new MLContext();
                var dataView = CreateDataView(mlContext, testData);
                var transformedData = model.Transform(dataView);

                if (testData.PredictionKind == PredictionKind.BinaryClassification)
                {
                    var metrics = mlContext.BinaryClassification.Evaluate(transformedData);
                    return metrics.PositivePrecision;
                }
                else
                {
                    var metrics = mlContext.MulticlassClassification.Evaluate(transformedData);
                    return metrics.MacroPrecision;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating precision metric");
                throw new InvalidOperationException("Failed to calculate precision metric", ex);
            }
        }

        /// <summary>
        /// Calculates recall, which is the ratio of true positives to total actual positives.
        /// Only applicable for classification tasks.
        /// </summary>
        private double CalculateRecall(ITransformer model, TestData testData)
        {
            try
            {
                if (testData.PredictionKind == PredictionKind.Regression)
                {
                    throw new ArgumentException("Recall is not applicable for regression tasks");
                }

                var mlContext = new MLContext();
                var dataView = CreateDataView(mlContext, testData);
                var transformedData = model.Transform(dataView);

                if (testData.PredictionKind == PredictionKind.BinaryClassification)
                {
                    var metrics = mlContext.BinaryClassification.Evaluate(transformedData);
                    return metrics.Recall;
                }
                else
                {
                    var metrics = mlContext.MulticlassClassification.Evaluate(transformedData);
                    return metrics.MacroRecall;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating recall metric");
                throw new InvalidOperationException("Failed to calculate recall metric", ex);
            }
        }

        /// <summary>
        /// Creates an IDataView from the test data for ML.NET evaluation.
        /// </summary>
        private IDataView CreateDataView(MLContext mlContext, TestData testData)
        {
            try
            {
                // Convert features dictionary to feature vector
                var featureValues = testData.Features.Values.Select(v => Convert.ToSingle(v)).ToArray();
                var data = new List<ModelTestData>
                {
                    new ModelTestData
                    {
                        Features = featureValues,
                        Label = testData.ExpectedValue
                    }
                };

                return mlContext.Data.LoadFromEnumerable(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating data view from test data");
                throw new InvalidOperationException("Failed to create data view", ex);
            }
        }

        private class ModelTestData
        {
            [VectorType(4)]
            public float[] Features { get; set; } = Array.Empty<float>();
            
            public float Label { get; set; }
        }

        private class ModelValidationState
        {
            public ITransformer Model { get; set; } = null!;
            public DateTime StartTime { get; set; }
            public List<PredictionResult> Predictions { get; set; } = new();
        }

        private class PredictionResult
        {
            public DateTime Timestamp { get; set; }
            public string Key { get; set; } = "";
            public double ActualValue { get; set; }
            public double PredictedValue { get; set; }
            public double Latency { get; set; }
            public Dictionary<string, object> Features { get; set; } = new();
        }

        private class ABTestState
        {
            public string ModelAId { get; set; } = "";
            public string ModelBId { get; set; } = "";
            public ABTestConfig Config { get; set; } = new();
            public DateTime StartTime { get; set; }
            public List<PredictionResult> ResultsA { get; set; } = new();
            public List<PredictionResult> ResultsB { get; set; } = new();
        }

        public class ABTestConfig
        {
            public TimeSpan Duration { get; set; } = TimeSpan.FromDays(7);
            public int MinimumSampleSize { get; set; } = 1000;
            public double SignificanceLevel { get; set; } = 0.05;
            public bool TrackLatency { get; set; } = true;
        }

        public class ABTestResults
        {
            public string TestId { get; set; } = "";
            public ModelStats ModelAStats { get; set; } = new();
            public ModelStats ModelBStats { get; set; } = new();
            public SignificanceTestResult SignificanceTest { get; set; } = new();
            public string Winner { get; set; } = "";
            public TimeSpan Duration { get; set; }
            public int SampleSizeA { get; set; }
            public int SampleSizeB { get; set; }
        }

        public class ModelStats
        {
            public double Accuracy { get; set; }
            public double StandardError { get; set; }
            public double MeanLatency { get; set; }
            public double LatencyP95 { get; set; }
            public int SampleSize { get; set; }
        }

        public class SignificanceTestResult
        {
            public string TestType { get; set; } = "";
            public double ZScore { get; set; }
            public double PValue { get; set; }
            public bool IsSignificant { get; set; }
        }
    }
}