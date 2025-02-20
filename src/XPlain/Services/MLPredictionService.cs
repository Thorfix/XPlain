using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class MLPredictionService
    {
        private readonly ICacheMonitoringService _monitoringService;
        private readonly Dictionary<string, List<double>> _metricHistory;
        private readonly int _historyWindow = 100;  // Number of data points to keep
        private readonly double _confidenceThreshold = 0.85;

        public MLPredictionService(ICacheMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
            _metricHistory = new Dictionary<string, List<double>>();
        }

        public async Task<Dictionary<string, PredictionResult>> PredictPerformanceMetrics()
        {
            var currentMetrics = await _monitoringService.GetPerformanceMetricsAsync();
            UpdateMetricHistory(currentMetrics);

            var predictions = new Dictionary<string, PredictionResult>();
            foreach (var metric in currentMetrics.Keys)
            {
                if (_metricHistory.ContainsKey(metric))
                {
                    var history = _metricHistory[metric];
                    var prediction = CalculatePrediction(history);
                    predictions[metric] = prediction;
                }
            }

            return predictions;
        }

        public async Task<List<PredictedAlert>> GetPredictedAlerts()
        {
            var predictions = await PredictPerformanceMetrics();
            var alerts = new List<PredictedAlert>();
            var thresholds = await _monitoringService.GetCurrentThresholdsAsync();

            foreach (var (metric, prediction) in predictions)
            {
                if (ShouldGenerateAlert(metric, prediction, thresholds))
                {
                    alerts.Add(new PredictedAlert
                    {
                        Metric = metric,
                        PredictedValue = prediction.Value,
                        Confidence = prediction.Confidence,
                        Timestamp = DateTime.UtcNow,
                        Severity = DetermineSeverity(metric, prediction.Value, thresholds),
                        TimeToImpact = prediction.TimeToImpact
                    });
                }
            }

            return alerts;
        }

        public async Task<Dictionary<string, TrendAnalysis>> AnalyzeTrends()
        {
            var trends = new Dictionary<string, TrendAnalysis>();
            foreach (var (metric, history) in _metricHistory)
            {
                if (history.Count >= _historyWindow)
                {
                    trends[metric] = new TrendAnalysis
                    {
                        Trend = CalculateTrend(history),
                        Seasonality = DetectSeasonality(history),
                        Volatility = CalculateVolatility(history)
                    };
                }
            }
            return trends;
        }

        private void UpdateMetricHistory(Dictionary<string, double> currentMetrics)
        {
            foreach (var (metric, value) in currentMetrics)
            {
                if (!_metricHistory.ContainsKey(metric))
                {
                    _metricHistory[metric] = new List<double>();
                }

                var history = _metricHistory[metric];
                history.Add(value);
                if (history.Count > _historyWindow)
                {
                    history.RemoveAt(0);
                }
            }
        }

        private PredictionResult CalculatePrediction(List<double> history)
        {
            // Simple linear regression for prediction
            var n = history.Count;
            var x = Enumerable.Range(0, n).ToList();
            var y = history;

            var meanX = x.Average();
            var meanY = y.Average();

            var covariance = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
            var varianceX = x.Select(xi => Math.Pow(xi - meanX, 2)).Sum();

            var slope = covariance / varianceX;
            var intercept = meanY - slope * meanX;

            var predictedValue = slope * (n + 5) + intercept; // Predict 5 steps ahead
            var confidence = CalculateConfidence(history, x, slope, intercept);
            var timeToImpact = EstimateTimeToImpact(slope, history.Last(), predictedValue);

            return new PredictionResult
            {
                Value = predictedValue,
                Confidence = confidence,
                TimeToImpact = timeToImpact
            };
        }

        private double CalculateConfidence(List<double> actual, List<double> x, double slope, double intercept)
        {
            var predicted = x.Select(xi => slope * xi + intercept);
            var errors = actual.Zip(predicted, (a, p) => Math.Pow(a - p, 2));
            var mse = errors.Average();
            return Math.Exp(-mse); // Transform MSE to confidence score between 0 and 1
        }

        private TimeSpan EstimateTimeToImpact(double slope, double current, double predicted)
        {
            if (Math.Abs(slope) < 0.0001) return TimeSpan.MaxValue;
            var timeSteps = Math.Abs((predicted - current) / slope);
            return TimeSpan.FromMinutes(timeSteps * 5); // Assuming 5-minute intervals
        }

        private TrendDirection CalculateTrend(List<double> history)
        {
            var recentValues = history.TakeLast(10).ToList();
            var slope = (recentValues.Last() - recentValues.First()) / recentValues.Count;
            
            if (slope > 0.01) return TrendDirection.Increasing;
            if (slope < -0.01) return TrendDirection.Decreasing;
            return TrendDirection.Stable;
        }

        private double DetectSeasonality(List<double> history)
        {
            // Simple autocorrelation-based seasonality detection
            var mean = history.Average();
            var normalized = history.Select(x => x - mean).ToList();
            
            var autocorrelation = Enumerable.Range(1, history.Count / 2)
                .Select(lag => CalculateAutocorrelation(normalized, lag))
                .ToList();
            
            return autocorrelation.Max();
        }

        private double CalculateAutocorrelation(List<double> data, int lag)
        {
            var n = data.Count - lag;
            var sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                sum += data[i] * data[i + lag];
            }
            return sum / n;
        }

        private double CalculateVolatility(List<double> history)
        {
            var changes = history.Zip(history.Skip(1), (a, b) => Math.Abs(b - a));
            return changes.Average();
        }

        private bool ShouldGenerateAlert(string metric, PredictionResult prediction, MonitoringThresholds thresholds)
        {
            if (prediction.Confidence < _confidenceThreshold)
                return false;

            return metric switch
            {
                "CacheHitRate" => prediction.Value < thresholds.MinHitRatio,
                "MemoryUsage" => prediction.Value > thresholds.MaxMemoryUsageMB,
                "AverageResponseTime" => prediction.Value > thresholds.MaxResponseTimeMs,
                _ => false
            };
        }

        private string DetermineSeverity(string metric, double predictedValue, MonitoringThresholds thresholds)
        {
            var severity = "Info";
            switch (metric)
            {
                case "CacheHitRate":
                    if (predictedValue < thresholds.MinHitRatio * 0.5) severity = "Critical";
                    else if (predictedValue < thresholds.MinHitRatio * 0.8) severity = "Warning";
                    break;
                case "MemoryUsage":
                    if (predictedValue > thresholds.MaxMemoryUsageMB * 1.5) severity = "Critical";
                    else if (predictedValue > thresholds.MaxMemoryUsageMB * 1.2) severity = "Warning";
                    break;
                case "AverageResponseTime":
                    if (predictedValue > thresholds.MaxResponseTimeMs * 2) severity = "Critical";
                    else if (predictedValue > thresholds.MaxResponseTimeMs * 1.5) severity = "Warning";
                    break;
            }
            return severity;
        }
    }

    public class PredictionResult
    {
        public double Value { get; set; }
        public double Confidence { get; set; }
        public TimeSpan TimeToImpact { get; set; }
    }

    public class PredictedAlert
    {
        public string Metric { get; set; }
        public double PredictedValue { get; set; }
        public double Confidence { get; set; }
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; }
        public TimeSpan TimeToImpact { get; set; }
    }

    public class TrendAnalysis
    {
        public TrendDirection Trend { get; set; }
        public double Seasonality { get; set; }
        public double Volatility { get; set; }
    }

    public enum TrendDirection
    {
        Increasing,
        Decreasing,
        Stable
    }
}