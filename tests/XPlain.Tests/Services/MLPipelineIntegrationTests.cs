using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Moq;
using Xunit;
using XPlain.Services;
using XPlain.Services.Models;

namespace XPlain.Tests.Services
{
    public class MLPipelineIntegrationTests
    {
        private readonly ICacheMonitoringService _monitoringService;
        private readonly IMLModelTrainingService _modelTrainingService;
        private readonly IMLModelValidationService _modelValidationService;
        private readonly MLPredictionService _predictionService;
        private readonly IAutomaticCacheOptimizer _cacheOptimizer;

        public MLPipelineIntegrationTests()
        {
            // Set up real services for integration testing
            _monitoringService = new CacheMonitoringService();
            _modelValidationService = new MLModelValidationService();
            _modelTrainingService = new MLModelTrainingService(_monitoringService);
            _predictionService = new MLPredictionService(_monitoringService, _modelTrainingService);
            _cacheOptimizer = new AutomaticCacheOptimizer(_predictionService, _monitoringService);
        }

        [Fact]
        public async Task EndToEnd_ModelTrainingAndPrediction_SuccessfullyOptimizesCache()
        {
            // Arrange
            var testData = GenerateTrainingData();
            await SeedTrainingData(testData);

            // Act - Train Models
            await _modelTrainingService.TrainModels();

            // Assert - Model Creation
            var hitRateModel = await _modelTrainingService.GetLatestModel("CacheHitRate");
            Assert.NotNull(hitRateModel);
            Assert.NotNull(hitRateModel.Transformer);
            Assert.Equal("CacheHitRate", hitRateModel.Metadata.MetricType);

            // Act - Model Validation
            var isValid = await _modelValidationService.ValidateModel(hitRateModel);
            Assert.True(isValid, "Model validation failed");

            // Act - Make Predictions
            var predictions = await _predictionService.PredictPerformanceMetrics();
            
            // Assert - Predictions
            Assert.NotNull(predictions);
            Assert.True(predictions.ContainsKey("CacheHitRate"));
            Assert.True(predictions["CacheHitRate"].Confidence >= 0.7, 
                "Model confidence should be at least 70%");

            // Act - Optimize Cache
            var optimizationResult = await _cacheOptimizer.OptimizeCache();
            
            // Assert - Optimization
            Assert.True(optimizationResult.Success);
            Assert.NotNull(optimizationResult.OptimizationMetrics);
            Assert.True(optimizationResult.OptimizationMetrics.ContainsKey("CacheHitRate"));
        }

        [Fact]
        public async Task ModelVersioning_WhenNewDataAvailable_UpgradesModel()
        {
            // Arrange
            var initialData = GenerateTrainingData();
            await SeedTrainingData(initialData);
            await _modelTrainingService.TrainModels();
            var initialModel = await _modelTrainingService.GetLatestModel("CacheHitRate");

            // Act - Add new training data
            var newData = GenerateTrainingData(offset: TimeSpan.FromDays(1));
            await SeedTrainingData(newData);
            await _modelTrainingService.TrainModels();
            var newModel = await _modelTrainingService.GetLatestModel("CacheHitRate");

            // Assert
            Assert.NotEqual(initialModel.Metadata.Version, newModel.Metadata.Version);
            Assert.True(newModel.Metadata.TrainingDate > initialModel.Metadata.TrainingDate);
        }

        [Fact]
        public async Task DataDriftDetection_WithDriftingMetrics_TriggersRetraining()
        {
            // Arrange
            var baselineData = GenerateTrainingData();
            await SeedTrainingData(baselineData);
            await _modelTrainingService.TrainModels();
            var initialModel = await _modelTrainingService.GetLatestModel("CacheHitRate");

            // Act - Introduce drift
            var driftedData = GenerateDriftedTrainingData();
            await SeedTrainingData(driftedData);
            
            // Trigger drift detection
            var driftDetected = await _modelTrainingService.CheckForDataDrift();
            
            // Assert
            Assert.True(driftDetected, "Data drift should be detected");
            
            // Act - Automatic retraining
            await _modelTrainingService.TrainModels();
            var newModel = await _modelTrainingService.GetLatestModel("CacheHitRate");
            
            // Assert
            Assert.NotEqual(initialModel.Metadata.Version, newModel.Metadata.Version);
        }

        [Fact]
        public async Task SafetyChecks_WithProblematicModel_PreventsDeployment()
        {
            // Arrange
            var problematicData = GenerateProblematicTrainingData();
            await SeedTrainingData(problematicData);
            await _modelTrainingService.TrainModels();
            var problematicModel = await _modelTrainingService.GetLatestModel("CacheHitRate");

            // Act
            var validationResult = await _modelValidationService.ValidateModel(problematicModel);
            var safetyMetrics = await _modelValidationService.GetModelHealthMetrics(problematicModel);

            // Assert
            Assert.False(validationResult, "Problematic model should fail validation");
            Assert.True(safetyMetrics.ContainsKey("Stability"));
            Assert.True(safetyMetrics["Stability"] < 0.7, "Stability score should be low for problematic model");
        }

        private List<PerformanceMetrics> GenerateTrainingData(TimeSpan? offset = null)
        {
            var data = new List<PerformanceMetrics>();
            var baseTime = DateTime.UtcNow.AddHours(-24) + (offset ?? TimeSpan.Zero);
            var random = new Random(42);

            for (int i = 0; i < 300; i++)
            {
                data.Add(new PerformanceMetrics
                {
                    Timestamp = baseTime.AddMinutes(i * 5),
                    CacheHitRate = 0.8 + random.NextDouble() * 0.2,
                    MemoryUsage = 1000 + random.Next(0, 500),
                    AverageResponseTime = 100 + random.Next(0, 50),
                    SystemCpuUsage = 0.5 + random.NextDouble() * 0.3,
                    SystemMemoryUsage = 0.6 + random.NextDouble() * 0.2,
                    ActiveConnections = 100 + random.Next(0, 50)
                });
            }

            return data;
        }

        private List<PerformanceMetrics> GenerateDriftedTrainingData()
        {
            var data = new List<PerformanceMetrics>();
            var baseTime = DateTime.UtcNow.AddHours(-24);
            var random = new Random(42);

            for (int i = 0; i < 300; i++)
            {
                data.Add(new PerformanceMetrics
                {
                    Timestamp = baseTime.AddMinutes(i * 5),
                    CacheHitRate = 0.4 + random.NextDouble() * 0.2, // Significantly worse performance
                    MemoryUsage = 2000 + random.Next(0, 500), // Higher memory usage
                    AverageResponseTime = 200 + random.Next(0, 50), // Slower response times
                    SystemCpuUsage = 0.8 + random.NextDouble() * 0.2,
                    SystemMemoryUsage = 0.9 + random.NextDouble() * 0.1,
                    ActiveConnections = 200 + random.Next(0, 50)
                });
            }

            return data;
        }

        private List<PerformanceMetrics> GenerateProblematicTrainingData()
        {
            var data = new List<PerformanceMetrics>();
            var baseTime = DateTime.UtcNow.AddHours(-24);
            var random = new Random(42);

            for (int i = 0; i < 300; i++)
            {
                // Generate highly unstable and inconsistent metrics
                data.Add(new PerformanceMetrics
                {
                    Timestamp = baseTime.AddMinutes(i * 5),
                    CacheHitRate = random.NextDouble(), // Completely random performance
                    MemoryUsage = random.Next(500, 3000), // Highly variable memory usage
                    AverageResponseTime = random.Next(50, 500), // Unstable response times
                    SystemCpuUsage = random.NextDouble(),
                    SystemMemoryUsage = random.NextDouble(),
                    ActiveConnections = random.Next(50, 300)
                });
            }

            return data;
        }

        private async Task SeedTrainingData(List<PerformanceMetrics> data)
        {
            foreach (var metrics in data)
            {
                await _monitoringService.RecordMetrics(metrics);
            }
        }
    }
}