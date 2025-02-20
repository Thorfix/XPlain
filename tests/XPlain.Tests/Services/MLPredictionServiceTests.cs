using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
using XPlain.Services;

namespace XPlain.Tests.Services
{
    public class MLPredictionServiceTests
    {
        private readonly Mock<ICacheMonitoringService> _mockMonitoringService;
        private readonly Mock<IMLModelTrainingService> _mockModelTrainingService;
        private readonly MLPredictionService _predictionService;

        public MLPredictionServiceTests()
        {
            _mockMonitoringService = new Mock<ICacheMonitoringService>();
            _mockModelTrainingService = new Mock<IMLModelTrainingService>();
            _predictionService = new MLPredictionService(
                _mockMonitoringService.Object,
                _mockModelTrainingService.Object);
        }

        [Fact]
        public async Task PredictPerformanceMetrics_WithMLModel_ReturnsPredictions()
        {
            // Arrange
            var currentMetrics = new Dictionary<string, double>
            {
                ["CacheHitRate"] = 0.85,
                ["MemoryUsage"] = 1200,
                ["AverageResponseTime"] = 120
            };

            _mockMonitoringService.Setup(m => m.GetPerformanceMetricsAsync())
                .ReturnsAsync(currentMetrics);

            var mockModel = new MLModel
            {
                Transformer = null, // Mock transformer would be set here
                Metadata = new ModelMetadata
                {
                    MetricType = "CacheHitRate",
                    Version = "1.0.0"
                }
            };

            _mockModelTrainingService.Setup(m => m.GetLatestModel(It.IsAny<string>()))
                .ReturnsAsync(mockModel);

            _mockModelTrainingService.Setup(m => m.ValidateModel(It.IsAny<MLModel>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            // Act
            var predictions = await _predictionService.PredictPerformanceMetrics();

            // Assert
            Assert.NotNull(predictions);
            Assert.True(predictions.ContainsKey("CacheHitRate"));
            Assert.True(predictions["CacheHitRate"].Confidence > 0);
        }

        [Fact]
        public async Task PredictPerformanceMetrics_WithoutMLModel_UsesFallbackPrediction()
        {
            // Arrange
            var currentMetrics = new Dictionary<string, double>
            {
                ["CacheHitRate"] = 0.85,
                ["MemoryUsage"] = 1200,
                ["AverageResponseTime"] = 120
            };

            _mockMonitoringService.Setup(m => m.GetPerformanceMetricsAsync())
                .ReturnsAsync(currentMetrics);

            _mockModelTrainingService.Setup(m => m.GetLatestModel(It.IsAny<string>()))
                .ReturnsAsync((MLModel)null);

            // Act
            var predictions = await _predictionService.PredictPerformanceMetrics();

            // Assert
            Assert.NotNull(predictions);
            Assert.True(predictions.ContainsKey("CacheHitRate"));
            Assert.True(predictions["CacheHitRate"].Confidence > 0);
        }

        [Fact]
        public async Task GetPredictedAlerts_WithDegradation_ReturnsAlerts()
        {
            // Arrange
            var currentMetrics = new Dictionary<string, double>
            {
                ["CacheHitRate"] = 0.65, // Degraded performance
                ["MemoryUsage"] = 1200,
                ["AverageResponseTime"] = 120
            };

            _mockMonitoringService.Setup(m => m.GetPerformanceMetricsAsync())
                .ReturnsAsync(currentMetrics);

            var mockThresholds = new MonitoringThresholds
            {
                MinHitRatio = 0.8,
                MaxMemoryUsageMB = 2000,
                MaxResponseTimeMs = 200
            };

            _mockMonitoringService.Setup(m => m.GetCurrentThresholdsAsync())
                .ReturnsAsync(mockThresholds);

            // Act
            var alerts = await _predictionService.GetPredictedAlerts();

            // Assert
            Assert.NotEmpty(alerts);
            var alert = alerts[0];
            Assert.Equal("CacheHitRate", alert.Metric);
            Assert.True(alert.PredictedValue < mockThresholds.MinHitRatio);
        }
    }
}