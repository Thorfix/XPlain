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
        private readonly Dictionary<string, List<Pattern>> _degradationPatterns;
        private readonly Dictionary<string, List<PrecursorPattern>> _precursorPatterns;
        private readonly int _historyWindow = 100;  // Number of data points to keep
        private readonly double _confidenceThreshold = 0.85;
        private readonly int _patternWindow = 10;   // Window size for pattern detection
        private readonly int _precursorWindow = 20; // Window size for precursor detection

        public MLPredictionService(ICacheMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
            _metricHistory = new Dictionary<string, List<double>>();
            _degradationPatterns = new Dictionary<string, List<Pattern>>();
            _precursorPatterns = new Dictionary<string, List<PrecursorPattern>>();
        }

        private class PrecursorPattern
        {
            public List<MetricSequence> Sequences { get; set; } = new();
            public TimeSpan LeadTime { get; set; }
            public string TargetIssue { get; set; }
            public double Confidence { get; set; }
            public int OccurrenceCount { get; set; }
            public Dictionary<string, (double Min, double Max)> Thresholds { get; set; }
        }

        private class MetricSequence
        {
            public string MetricName { get; set; }
            public List<double> Values { get; set; }
            public List<double> Derivatives { get; set; }
            public Dictionary<string, double> Correlations { get; set; }
        }

        private class Pattern
        {
            public List<double> Sequence { get; set; } = new();
            public string IssueType { get; set; }
            public double Severity { get; set; }
            public int OccurrenceCount { get; set; }
            public TimeSpan TimeToIssue { get; set; }
        }

        private async Task UpdateDegradationPatterns(string metric, double currentValue)
        {
            var history = _metricHistory[metric];

            // Check if this is a degradation point
            bool isDegradation = IsPerformanceDegradation(metric, currentValue);
            if (isDegradation)
            {
                // Extract the pattern that led to this degradation
                var pattern = history.Skip(Math.Max(0, history.Count - _patternWindow - 1))
                                   .Take(_patternWindow)
                                   .ToList();

                // Normalize the pattern
                var normalizedPattern = NormalizeSequence(pattern);
                
                // Store or update the pattern
                if (!_degradationPatterns.ContainsKey(metric))
                {
                    _degradationPatterns[metric] = new List<Pattern>();
                }

                var existingPattern = _degradationPatterns[metric]
                    .FirstOrDefault(p => IsSimilarPattern(p.Sequence, normalizedPattern));

                if (existingPattern != null)
                {
                    existingPattern.OccurrenceCount++;
                    // Update the average time to issue
                    existingPattern.TimeToIssue = TimeSpan.FromTicks(
                        (existingPattern.TimeToIssue.Ticks * (existingPattern.OccurrenceCount - 1) +
                         TimeSpan.FromMinutes(5).Ticks) / existingPattern.OccurrenceCount);
                }
                else
                {
                    _degradationPatterns[metric].Add(new Pattern
                    {
                        Sequence = normalizedPattern,
                        IssueType = GetIssueType(metric, currentValue),
                        Severity = CalculateIssueSeverity(metric, currentValue),
                        OccurrenceCount = 1,
                        TimeToIssue = TimeSpan.FromMinutes(5)
                    });
                }
            }

            await AnalyzePrecursorPatterns(metric, currentValue);
        }

        private async Task AnalyzePrecursorPatterns(string metric, double currentValue)
        {
            if (!_metricHistory.ContainsKey(metric) || 
                _metricHistory[metric].Count < _precursorWindow) return;

            // Detect performance degradation
            var isDegradation = IsPerformanceDegradation(metric, currentValue);
            if (!isDegradation) return;

            // Analyze all metrics for precursor patterns
            var allMetrics = _metricHistory.Keys.ToList();
            var timeWindow = TimeSpan.FromMinutes(_precursorWindow * 5); // Assuming 5-minute intervals

            var precursorCandidates = new List<(string Metric, List<double> Pattern, TimeSpan LeadTime)>();
            
            foreach (var metricName in allMetrics)
            {
                var metricHistory = _metricHistory[metricName].TakeLast(_precursorWindow).ToList();
                var derivatives = CalculateDerivatives(metricHistory);
                var correlations = CalculateCorrelations(metricName, allMetrics);

                // Look for significant changes that precede the degradation
                var changePoints = DetectChangePoints(derivatives);
                foreach (var changePoint in changePoints)
                {
                    var leadTime = TimeSpan.FromMinutes((changePoint - _precursorWindow) * 5);
                    if (leadTime > TimeSpan.Zero && leadTime <= timeWindow)
                    {
                        precursorCandidates.Add((
                            metricName,
                            metricHistory.Skip(changePoint).Take(_patternWindow).ToList(),
                            leadTime
                        ));
                    }
                }
            }

            // Update precursor patterns
            foreach (var candidate in precursorCandidates)
            {
                await UpdatePrecursorPattern(
                    candidate.Metric,
                    candidate.Pattern,
                    candidate.LeadTime,
                    metric,
                    currentValue
                );
            }

            // Check if this is a degradation point
            bool isDegradation = IsPerformanceDegradation(metric, currentValue);
            if (isDegradation)
            {
                // Extract the pattern that led to this degradation
                var pattern = history.Skip(history.Count - _patternWindow - 1)
                                   .Take(_patternWindow)
                                   .ToList();

                // Normalize the pattern
                var normalizedPattern = NormalizeSequence(pattern);
                
                // Store or update the pattern
                if (!_degradationPatterns.ContainsKey(metric))
                {
                    _degradationPatterns[metric] = new List<Pattern>();
                }

                var existingPattern = _degradationPatterns[metric]
                    .FirstOrDefault(p => IsSimilarPattern(p.Sequence, normalizedPattern));

                if (existingPattern != null)
                {
                    existingPattern.OccurrenceCount++;
                    // Update the average time to issue
                    existingPattern.TimeToIssue = TimeSpan.FromTicks(
                        (existingPattern.TimeToIssue.Ticks * (existingPattern.OccurrenceCount - 1) +
                         TimeSpan.FromMinutes(5).Ticks) / existingPattern.OccurrenceCount);
                }
                else
                {
                    _degradationPatterns[metric].Add(new Pattern
                    {
                        Sequence = normalizedPattern,
                        IssueType = GetIssueType(metric, currentValue),
                        Severity = CalculateIssueSeverity(metric, currentValue),
                        OccurrenceCount = 1,
                        TimeToIssue = TimeSpan.FromMinutes(5)
                    });
                }
            }
        }

        private List<double> CalculateDerivatives(List<double> values)
        {
            var derivatives = new List<double>();
            for (int i = 1; i < values.Count; i++)
            {
                derivatives.Add(values[i] - values[i - 1]);
            }
            return derivatives;
        }

        private Dictionary<string, double> CalculateCorrelations(string baseMetric, List<string> allMetrics)
        {
            var correlations = new Dictionary<string, double>();
            var baseValues = _metricHistory[baseMetric];

            foreach (var otherMetric in allMetrics)
            {
                if (otherMetric == baseMetric) continue;

                var otherValues = _metricHistory[otherMetric];
                var correlation = CalculatePearsonCorrelation(
                    baseValues.TakeLast(_precursorWindow).ToList(),
                    otherValues.TakeLast(_precursorWindow).ToList()
                );
                correlations[otherMetric] = correlation;
            }

            return correlations;
        }

        private double CalculatePearsonCorrelation(List<double> x, List<double> y)
        {
            var n = Math.Min(x.Count, y.Count);
            var meanX = x.Take(n).Average();
            var meanY = y.Take(n).Average();

            var sumXY = x.Take(n).Zip(y.Take(n), (a, b) => (a - meanX) * (b - meanY)).Sum();
            var sumX2 = x.Take(n).Sum(a => Math.Pow(a - meanX, 2));
            var sumY2 = y.Take(n).Sum(b => Math.Pow(b - meanY, 2));

            return sumXY / Math.Sqrt(sumX2 * sumY2);
        }

        private List<int> DetectChangePoints(List<double> derivatives)
        {
            var changePoints = new List<int>();
            var mean = derivatives.Average();
            var stdDev = Math.Sqrt(derivatives.Select(x => Math.Pow(x - mean, 2)).Average());
            var threshold = stdDev * 2; // 2 standard deviations

            for (int i = 1; i < derivatives.Count; i++)
            {
                if (Math.Abs(derivatives[i] - derivatives[i - 1]) > threshold)
                {
                    changePoints.Add(i);
                }
            }

            return changePoints;
        }

        private async Task UpdatePrecursorPattern(
            string precursorMetric,
            List<double> pattern,
            TimeSpan leadTime,
            string targetMetric,
            double degradationValue)
        {
            if (!_precursorPatterns.ContainsKey(targetMetric))
            {
                _precursorPatterns[targetMetric] = new List<PrecursorPattern>();
            }

            var normalizedPattern = NormalizeSequence(pattern);
            var existingPattern = _precursorPatterns[targetMetric]
                .FirstOrDefault(p => p.Sequences
                    .Any(s => s.MetricName == precursorMetric && 
                             IsSimilarPattern(s.Values, normalizedPattern)));

            if (existingPattern != null)
            {
                existingPattern.OccurrenceCount++;
                existingPattern.Confidence = Math.Min(0.95, 
                    existingPattern.Confidence + (1.0 / existingPattern.OccurrenceCount));
                existingPattern.LeadTime = TimeSpan.FromTicks(
                    (existingPattern.LeadTime.Ticks * (existingPattern.OccurrenceCount - 1) +
                     leadTime.Ticks) / existingPattern.OccurrenceCount);
            }
            else
            {
                var newPattern = new PrecursorPattern
                {
                    Sequences = new List<MetricSequence>
                    {
                        new MetricSequence
                        {
                            MetricName = precursorMetric,
                            Values = normalizedPattern,
                            Derivatives = CalculateDerivatives(pattern),
                            Correlations = CalculateCorrelations(precursorMetric, _metricHistory.Keys.ToList())
                        }
                    },
                    LeadTime = leadTime,
                    TargetIssue = $"{targetMetric}Degradation",
                    Confidence = 0.5,
                    OccurrenceCount = 1,
                    Thresholds = new Dictionary<string, (double Min, double Max)>
                    {
                        [precursorMetric] = (
                            pattern.Min() * 0.9,
                            pattern.Max() * 1.1
                        )
                    }
                };
                _precursorPatterns[targetMetric].Add(newPattern);
            }
        }

        private bool IsPerformanceDegradation(string metric, double currentValue)
        {
            var history = _metricHistory[metric];
            var baseline = history.Skip(history.Count - _patternWindow).Average();

            return metric.ToLower() switch
            {
                "cachehitrate" => currentValue < baseline * 0.8,
                "memoryusage" => currentValue > baseline * 1.2,
                "averageresponsetime" => currentValue > baseline * 1.5,
                _ => false
            };
        }

        private double GetPatternBasedPrediction(double currentValue, Pattern pattern)
        {
            // Predict based on pattern's typical progression
            return pattern.IssueType.ToLower() switch
            {
                "lowhitrate" => currentValue * 0.8, // Predict 20% degradation
                "highmemoryusage" => currentValue * 1.2, // Predict 20% increase
                "slowresponse" => currentValue * 1.5, // Predict 50% slowdown
                _ => currentValue
            };
        }

        public List<PrecursorPattern> GetActivePrecursorPatterns()
        {
            var activePatterns = new List<PrecursorPattern>();
            
            foreach (var (metric, patterns) in _precursorPatterns)
            {
                foreach (var pattern in patterns.Where(p => p.Confidence > 0.7))
                {
                    var isActive = pattern.Sequences.All(seq =>
                    {
                        var currentHistory = _metricHistory[seq.MetricName]
                            .TakeLast(seq.Values.Count)
                            .ToList();
                        var normalizedCurrent = NormalizeSequence(currentHistory);
                        return IsSimilarPattern(normalizedCurrent, seq.Values);
                    });

                    if (isActive)
                    {
                        activePatterns.Add(pattern);
                    }
                }
            }

            return activePatterns;
        }

        private List<double> NormalizeSequence(List<double> sequence)
        {
            var min = sequence.Min();
            var max = sequence.Max();
            var range = max - min;
            
            return range == 0 
                ? sequence.Select(_ => 0.0).ToList() 
                : sequence.Select(v => (v - min) / range).ToList();
        }

        private bool IsSimilarPattern(List<double> pattern1, List<double> pattern2)
        {
            if (pattern1.Count != pattern2.Count) return false;

            // Calculate Euclidean distance between patterns
            var distance = Math.Sqrt(
                pattern1.Zip(pattern2, (a, b) => Math.Pow(a - b, 2)).Sum()
            );

            return distance < 0.3; // Similarity threshold
        }

        private string GetIssueType(string metric, double value)
        {
            return metric.ToLower() switch
            {
                "cachehitrate" => "LowHitRate",
                "memoryusage" => "HighMemoryUsage",
                "averageresponsetime" => "SlowResponse",
                _ => "Unknown"
            };
        }

        private double CalculateIssueSeverity(string metric, double value)
        {
            var history = _metricHistory[metric];
            var baseline = history.Skip(history.Count - _patternWindow).Average();
            
            return metric.ToLower() switch
            {
                "cachehitrate" => Math.Max(0, (baseline - value) / baseline),
                "memoryusage" => Math.Max(0, (value - baseline) / baseline),
                "averageresponsetime" => Math.Max(0, (value - baseline) / baseline),
                _ => 0
            };
        }

        public async Task<Dictionary<string, PredictionResult>> PredictPerformanceMetrics()
        {
            var currentMetrics = await _monitoringService.GetPerformanceMetricsAsync();
            UpdateMetricHistory(currentMetrics);

            // Update degradation patterns for each metric
            foreach (var (metric, value) in currentMetrics)
            {
                await UpdateDegradationPatterns(metric, value);
            }

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
            var metric = _metricHistory.FirstOrDefault(x => x.Value == history).Key;
            var recentPattern = history.Skip(history.Count - _patternWindow).ToList();
            var normalizedRecent = NormalizeSequence(recentPattern);

            // Check for matching degradation patterns
            var matchingPattern = metric != null && _degradationPatterns.ContainsKey(metric)
                ? _degradationPatterns[metric]
                    .FirstOrDefault(p => IsSimilarPattern(p.Sequence, normalizedRecent))
                : null;

            // Combine pattern-based and regression-based predictions
            var n = history.Count;
            var x = Enumerable.Range(0, n).ToList();
            var y = history;

            var meanX = x.Average();
            var meanY = y.Average();

            var covariance = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
            var varianceX = x.Select(xi => Math.Pow(xi - meanX, 2)).Sum();

            var slope = covariance / varianceX;
            var intercept = meanY - slope * meanX;

            var regressionPrediction = slope * (n + 5) + intercept;
            var confidence = CalculateConfidence(history, x, slope, intercept);

            // If we have a matching pattern, adjust the prediction
            if (matchingPattern != null)
            {
                var patternConfidence = Math.Min(1.0, matchingPattern.OccurrenceCount / 10.0);
                var timeToImpact = matchingPattern.TimeToIssue;
                
                // Blend predictions based on pattern confidence
                var blendedPrediction = (regressionPrediction * (1 - patternConfidence)) +
                                      (GetPatternBasedPrediction(history.Last(), matchingPattern) * patternConfidence);
                
                // Increase confidence if pattern matches
                confidence = (confidence + patternConfidence) / 2;

                return new PredictionResult
                {
                    Value = blendedPrediction,
                    Confidence = confidence,
                    TimeToImpact = timeToImpact,
                    DetectedPattern = new PredictionPattern
                    {
                        Type = matchingPattern.IssueType,
                        Severity = matchingPattern.Severity,
                        Confidence = patternConfidence,
                        TimeToIssue = timeToImpact
                    }
                };
            }

            var regressionTimeToImpact = EstimateTimeToImpact(slope, history.Last(), regressionPrediction);
            
            return new PredictionResult
            {
                Value = regressionPrediction,
                Confidence = confidence,
                TimeToImpact = regressionTimeToImpact
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

    public class PredictionPattern
    {
        public string Type { get; set; }
        public double Severity { get; set; }
        public double Confidence { get; set; }
        public TimeSpan TimeToIssue { get; set; }
    }

    public class PredictionResult
    {
        public double Value { get; set; }
        public double Confidence { get; set; }
        public TimeSpan TimeToImpact { get; set; }
        public PredictionPattern DetectedPattern { get; set; }
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