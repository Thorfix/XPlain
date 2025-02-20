using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using System.Text.Json;

namespace XPlain.Services
{
    public interface IMLModelTrainingService
    {
        Task TrainModels();
        Task<MLModel> GetLatestModel(string metricType);
        Task<bool> ValidateModel(MLModel model, string metricType);
    }

    public class MLModelTrainingService : IMLModelTrainingService
    {
        private readonly ICacheMonitoringService _monitoringService;
        private readonly string _modelDirectory = "Models";
        private readonly MLContext _mlContext;
        private readonly Dictionary<string, ITransformer> _activeModels;
        private readonly int _trainHistoryHours = 24;
        private readonly double _validationSplit = 0.2;

        public MLModelTrainingService(ICacheMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
            _mlContext = new MLContext(seed: 1);
            _activeModels = new Dictionary<string, ITransformer>();
            
            if (!Directory.Exists(_modelDirectory))
            {
                Directory.CreateDirectory(_modelDirectory);
            }
        }

        public async Task TrainModels()
        {
            var metricTypes = new[] { "CacheHitRate", "MemoryUsage", "AverageResponseTime" };
            
            foreach (var metricType in metricTypes)
            {
                var data = await CollectTrainingData(metricType);
                if (data.Count > 0)
                {
                    await TrainModelForMetric(metricType, data);
                }
            }
        }

        private async Task<List<MetricDataPoint>> CollectTrainingData(string metricType)
        {
            var data = new List<MetricDataPoint>();
            var metrics = await _monitoringService.GetHistoricalMetrics(TimeSpan.FromHours(_trainHistoryHours));
            
            // Create sliding windows of data points
            const int windowSize = 10;
            for (int i = 0; i <= metrics.Count - windowSize; i++)
            {
                var window = metrics.Skip(i).Take(windowSize).ToList();
                var features = ExtractFeatures(window, metricType);
                var label = window.Last().GetMetricValue(metricType);
                
                data.Add(new MetricDataPoint
                {
                    Features = features,
                    MetricValue = label,
                    Timestamp = window.Last().Timestamp
                });
            }
            
            return data;
        }

        private float[] ExtractFeatures(List<PerformanceMetrics> window, string metricType)
        {
            var features = new List<float>();
            
            // Historical values
            features.AddRange(window.Select(m => (float)m.GetMetricValue(metricType)));
            
            // Rate of change
            for (int i = 1; i < window.Count; i++)
            {
                var change = (float)(window[i].GetMetricValue(metricType) - 
                                   window[i-1].GetMetricValue(metricType));
                features.Add(change);
            }
            
            // System load indicators
            features.Add((float)window.Last().SystemCpuUsage);
            features.Add((float)window.Last().SystemMemoryUsage);
            features.Add((float)window.Last().ActiveConnections);
            
            return features.ToArray();
        }

        private async Task TrainModelForMetric(string metricType, List<MetricDataPoint> data)
        {
            // Create training dataset
            var trainingData = _mlContext.Data.LoadFromEnumerable(data);
            
            // Split data
            var dataSplit = _mlContext.Data.TrainTestSplit(trainingData, testFraction: _validationSplit);
            
            // Define pipeline
            var pipeline = _mlContext.Transforms.Concatenate("Features", "Features")
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.Regression.Trainers.LbfgsPoissonRegression());
            
            // Train model
            var model = pipeline.Fit(dataSplit.TrainSet);
            
            // Evaluate
            var predictions = model.Transform(dataSplit.TestSet);
            var metrics = _mlContext.Regression.Evaluate(predictions);
            
            if (IsModelAcceptable(metrics))
            {
                await SaveModel(model, metricType);
                _activeModels[metricType] = model;
            }
        }

        private bool IsModelAcceptable(RegressionMetrics metrics)
        {
            return metrics.RSquared > 0.7 && metrics.MeanAbsoluteError < 0.2;
        }

        private async Task SaveModel(ITransformer model, string metricType)
        {
            var modelPath = Path.Combine(_modelDirectory, $"{metricType}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
            using (var stream = File.Create(modelPath))
            {
                _mlContext.Model.Save(model, null, stream);
            }
            
            await SaveModelMetadata(modelPath, metricType);
        }

        private async Task SaveModelMetadata(string modelPath, string metricType)
        {
            var metadata = new ModelMetadata
            {
                ModelPath = modelPath,
                MetricType = metricType,
                CreatedAt = DateTime.UtcNow,
                Version = GetNextVersion(metricType)
            };
            
            var metadataPath = Path.Combine(_modelDirectory, $"{metricType}_metadata.json");
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata));
        }

        private string GetNextVersion(string metricType)
        {
            var metadataPath = Path.Combine(_modelDirectory, $"{metricType}_metadata.json");
            if (!File.Exists(metadataPath)) return "1.0.0";
            
            var metadata = JsonSerializer.Deserialize<ModelMetadata>(
                File.ReadAllText(metadataPath));
            var version = Version.Parse(metadata.Version);
            return new Version(version.Major, version.Minor + 1, 0).ToString();
        }

        public async Task<MLModel> GetLatestModel(string metricType)
        {
            var metadataPath = Path.Combine(_modelDirectory, $"{metricType}_metadata.json");
            if (!File.Exists(metadataPath)) return null;
            
            var metadata = JsonSerializer.Deserialize<ModelMetadata>(
                File.ReadAllText(metadataPath));
            
            if (!File.Exists(metadata.ModelPath)) return null;
            
            using (var stream = File.OpenRead(metadata.ModelPath))
            {
                var model = _mlContext.Model.Load(stream, out var _);
                return new MLModel
                {
                    Transformer = model,
                    Metadata = metadata
                };
            }
        }

        public async Task<bool> ValidateModel(MLModel model, string metricType)
        {
            // Collect recent data for validation
            var validationData = await CollectTrainingData(metricType);
            if (validationData.Count == 0) return false;
            
            var dataView = _mlContext.Data.LoadFromEnumerable(validationData);
            var predictions = model.Transformer.Transform(dataView);
            var metrics = _mlContext.Regression.Evaluate(predictions);
            
            return IsModelAcceptable(metrics);
        }
    }

    public class MetricDataPoint
    {
        [VectorType(23)]
        public float[] Features { get; set; }
        
        public float MetricValue { get; set; }
        
        public DateTime Timestamp { get; set; }
    }

    public class ModelMetadata
    {
        public string ModelPath { get; set; }
        public string MetricType { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Version { get; set; }
    }

    public class MLModel
    {
        public ITransformer Transformer { get; set; }
        public ModelMetadata Metadata { get; set; }
    }
}