using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class MLPredictionService
    {
        private readonly Random _random = new Random();
        private readonly Dictionary<string, List<PredictedAlert>> _mockAlerts = new Dictionary<string, List<PredictedAlert>>();
        private readonly List<PrecursorPattern> _mockPatterns = new List<PrecursorPattern>();

        public MLPredictionService()
        {
            // Initialize with some mock data
            InitializeMockData();
        }

        private void InitializeMockData()
        {
            // Create some mock alerts
            _mockAlerts["CachePerformance"] = new List<PredictedAlert>
            {
                new PredictedAlert
                {
                    Type = "CacheHitRate",
                    Message = "Cache hit rate predicted to drop below warning threshold",
                    Severity = "Warning",
                    Confidence = 0.8,
                    TimeToImpact = TimeSpan.FromMinutes(30)
                },
                new PredictedAlert
                {
                    Type = "MemoryUsage",
                    Message = "Memory usage predicted to reach warning level",
                    Severity = "Warning",
                    Confidence = 0.75,
                    TimeToImpact = TimeSpan.FromMinutes(45)
                }
            };

            // Create some mock precursor patterns
            _mockPatterns.Add(new PrecursorPattern
            {
                TargetIssue = "High Response Time",
                Confidence = 0.85,
                LeadTime = TimeSpan.FromMinutes(20),
                Sequences = new List<MetricSequence>
                {
                    new MetricSequence
                    {
                        MetricName = "MemoryUsage",
                        Correlation = 0.8,
                        Values = new List<double> { 60, 65, 70, 75, 80 }
                    },
                    new MetricSequence
                    {
                        MetricName = "CacheHitRate",
                        Correlation = -0.7,
                        Values = new List<double> { 0.8, 0.75, 0.7, 0.65, 0.6 }
                    }
                }
            });
        }

        public async Task<double> PredictQueryValueAsync(string query)
        {
            // Simple mock implementation that returns a random value
            await Task.Delay(10); // Simulate processing time
            return 0.1 + _random.NextDouble() * 0.8; // Return a value between 0.1 and 0.9
        }

        public async Task<Dictionary<string, DateTime>> PredictOptimalTimingsAsync(List<string> keys)
        {
            var result = new Dictionary<string, DateTime>();
            var now = DateTime.UtcNow;

            foreach (var key in keys)
            {
                // Add a random time in the future (between 5 minutes and 2 hours)
                var minutesOffset = 5 + _random.Next(115);
                result[key] = now.AddMinutes(minutesOffset);
            }

            return result;
        }

        public async Task<List<PredictedAlert>> GetPredictedAlerts()
        {
            // Return the mock alerts for cache performance
            return _mockAlerts["CachePerformance"];
        }

        public List<PrecursorPattern> GetActivePrecursorPatterns()
        {
            // Return the mock precursor patterns
            return _mockPatterns;
        }

        public async Task<Dictionary<string, PredictionResult>> PredictPerformanceMetrics()
        {
            var now = DateTime.UtcNow;
            return new Dictionary<string, PredictionResult>
            {
                ["CacheHitRate"] = new PredictionResult
                {
                    Value = 0.6 + (_random.NextDouble() * 0.3),
                    Confidence = 0.7 + (_random.NextDouble() * 0.25),
                    TimeToImpact = TimeSpan.FromMinutes(15 + _random.Next(45))
                },
                ["MemoryUsage"] = new PredictionResult
                {
                    Value = 70 + (_random.NextDouble() * 20),
                    Confidence = 0.75 + (_random.NextDouble() * 0.2),
                    TimeToImpact = TimeSpan.FromMinutes(10 + _random.Next(50))
                },
                ["AverageResponseTime"] = new PredictionResult
                {
                    Value = 150 + (_random.NextDouble() * 100),
                    Confidence = 0.65 + (_random.NextDouble() * 0.25),
                    TimeToImpact = TimeSpan.FromMinutes(5 + _random.Next(25))
                }
            };
        }

        public async Task<Dictionary<string, TrendAnalysis>> AnalyzeTrends()
        {
            var now = DateTime.UtcNow;
            return new Dictionary<string, TrendAnalysis>
            {
                ["CacheHitRate"] = new TrendAnalysis
                {
                    Trend = GetRandomTrend(),
                    CurrentValue = 0.7 + (_random.NextDouble() * 0.2),
                    ProjectedValue = 0.6 + (_random.NextDouble() * 0.3),
                    ProjectionTime = now.AddHours(1),
                    ChangePercent = -5 - (_random.NextDouble() * 15) // -5% to -20%
                },
                ["MemoryUsage"] = new TrendAnalysis
                {
                    Trend = GetRandomTrend(),
                    CurrentValue = 70 + (_random.NextDouble() * 10),
                    ProjectedValue = 80 + (_random.NextDouble() * 15),
                    ProjectionTime = now.AddHours(1),
                    ChangePercent = 5 + (_random.NextDouble() * 15) // 5% to 20%
                },
                ["ResponseTime"] = new TrendAnalysis
                {
                    Trend = GetRandomTrend(),
                    CurrentValue = 100 + (_random.NextDouble() * 50),
                    ProjectedValue = 120 + (_random.NextDouble() * 80),
                    ProjectionTime = now.AddHours(1),
                    ChangePercent = 10 + (_random.NextDouble() * 20) // 10% to 30%
                }
            };
        }

        private string GetRandomTrend()
        {
            var trends = new[] { "Increasing", "Decreasing", "Stable" };
            return trends[_random.Next(trends.Length)];
        }
    }
}