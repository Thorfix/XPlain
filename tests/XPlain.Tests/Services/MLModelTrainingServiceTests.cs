using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
using XPlain.Services;

namespace XPlain.Tests.Services
{
    public class MLModelTrainingServiceTests
    {
        private readonly Mock<ICacheMonitoringService> _mockMonitoringService;
        private readonly IMLModelTrainingService _modelTrainingService;

        public MLModelTrainingServiceTests()
        {
            _mockMonitoringService = new Mock<ICacheMonitoringService>();
            _modelTrainingService = new MLModelTrainingService(_mockMonitoringService.Object);
        }

        [Fact]
        public async Task TrainModels_WithSufficientData_CreatesModels()
        {
            // Arrange
            var testData = GenerateTestData();
            _mockMonitoringService.Setup(m => m.GetHistoricalMetrics(It.IsAny<TimeSpan>()))
                .ReturnsAsync(testData);

            // Act
            await _modelTrainingService.TrainModels();

            // Assert
            var model = await _modelTrainingService.GetLatestModel("CacheHitRate");
            Assert.NotNull(model);
            Assert.NotNull(model.Transformer);
            Assert.NotNull(model.Metadata);
            Assert.Equal("CacheHitRate", model.Metadata.MetricType);
        }

        [Fact]
        public async Task ValidateModel_WithValidModel_ReturnsTrue()
        {
            // Arrange
            var testData = GenerateTestData();
            _mockMonitoringService.Setup(m => m.GetHistoricalMetrics(It.IsAny<TimeSpan>()))
                .ReturnsAsync(testData);
            await _modelTrainingService.TrainModels();
            var model = await _modelTrainingService.GetLatestModel("CacheHitRate");

            // Act
            var isValid = await _modelTrainingService.ValidateModel(model, "CacheHitRate");

            // Assert
            Assert.True(isValid);
        }

        private List<PerformanceMetrics> GenerateTestData()
        {
            var data = new List<PerformanceMetrics>();
            var baseTime = DateTime.UtcNow.AddHours(-24);
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
    }
}