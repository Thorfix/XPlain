using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class MLPredictionService
    {
        private readonly ICacheMonitoringService _monitoringService;
        private readonly IMLModelTrainingService _modelTrainingService;
        private readonly IAutomaticCacheOptimizer _cacheOptimizer;
        private readonly Dictionary<string, List<double>> _metricHistory;
        private readonly Dictionary<string, List<Pattern>> _degradationPatterns;
        private readonly Dictionary<string, List<PrecursorPattern>> _precursorPatterns;
        private readonly int _historyWindow = 100;  // Number of data points to keep
        private readonly object _modelsLock = new object();  // Lock for thread-safe model updates
        private readonly double _confidenceThreshold = 0.85;
        private readonly int _patternWindow = 10;   // Window size for pattern detection
        private readonly int _precursorWindow = 20; // Window size for precursor detection
        private readonly Dictionary<string, MLModel> _activeModels;
        private DateTime _lastModelTraining = DateTime.MinValue;
        private readonly TimeSpan _modelTrainingInterval = TimeSpan.FromHours(6);

        public MLPredictionService(
            ICacheMonitoringService monitoringService,
            IMLModelTrainingService modelTrainingService,
            IAutomaticCacheOptimizer cacheOptimizer)
        {
            _monitoringService = monitoringService;
            _modelTrainingService = modelTrainingService;
            _cacheOptimizer = cacheOptimizer;
            _metricHistory = new Dictionary<string, List<double>>();
            _degradationPatterns = new Dictionary<string, List<Pattern>>();
            _precursorPatterns = new Dictionary<string, List<PrecursorPattern>>();
            _activeModels = new Dictionary<string, MLModel>();
        }

        public async Task UpdateModelAsync(string actionType, MLModel newModel)
        {
            if (string.IsNullOrEmpty(actionType) || newModel == null)
                throw new ArgumentNullException(actionType == null ? nameof(actionType) : nameof(newModel));

            if (_activeModels.ContainsKey(actionType))
            {
                _activeModels[actionType] = newModel;
                _logger.LogInformation("Successfully updated ML model for {ActionType}", actionType);
            }
            else
            {
                _logger.LogWarning("Attempted to update non-existent model for {ActionType}", actionType);
            }
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
            public List<double> Derivatives { get; set; } = new();
            public Dictionary<string, double> Correlations { get; set; } = new();
            public PatternCluster Cluster { get; set; }
            public double Stability { get; set; } = 1.0;
            public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
            public List<PatternMutation> Mutations { get; set; } = new();
        }

        private class PatternCluster
        {
            public string Name { get; set; }
            public List<Pattern> Patterns { get; set; } = new();
            public Pattern Centroid { get; set; }
            public double Radius { get; set; }
            public PatternCluster Parent { get; set; }
            public List<PatternCluster> Children { get; set; } = new();
            public int Level { get; set; }
            public double Confidence { get; set; }
        }

        private class PatternMutation
        {
            public DateTime Timestamp { get; set; }
            public List<double> PreviousSequence { get; set; }
            public List<double> NewSequence { get; set; }
            public double MutationMagnitude { get; set; }
            public string MutationType { get; set; }
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
                
                // Calculate additional pattern features
                var derivatives = CalculateDerivatives(pattern);
                var correlations = CalculateCorrelations(metric, _metricHistory.Keys.ToList());

                // Find or create the appropriate pattern cluster
                var newPattern = new Pattern
                {
                    Sequence = normalizedPattern,
                    IssueType = GetIssueType(metric, currentValue),
                    Severity = CalculateIssueSeverity(metric, currentValue),
                    OccurrenceCount = 1,
                    TimeToIssue = TimeSpan.FromMinutes(5),
                    Derivatives = derivatives,
                    Correlations = correlations,
                    LastUpdated = DateTime.UtcNow
                };

                if (!_degradationPatterns.ContainsKey(metric))
                {
                    _degradationPatterns[metric] = new List<Pattern>();
                }

                var existingPattern = _degradationPatterns[metric]
                    .FirstOrDefault(p => IsSimilarPattern(p.Sequence, normalizedPattern));

                if (existingPattern != null)
                {
                    // Track pattern evolution
                    var mutation = new PatternMutation
                    {
                        Timestamp = DateTime.UtcNow,
                        PreviousSequence = existingPattern.Sequence.ToList(),
                        NewSequence = normalizedPattern,
                        MutationMagnitude = CalculatePatternDifference(existingPattern.Sequence, normalizedPattern),
                        MutationType = DetermineMutationType(existingPattern.Sequence, normalizedPattern)
                    };
                    existingPattern.Mutations.Add(mutation);

                    // Update pattern stability
                    existingPattern.Stability = CalculatePatternStability(existingPattern);
                    
                    // Update pattern attributes
                    existingPattern.OccurrenceCount++;
                    existingPattern.LastUpdated = DateTime.UtcNow;
                    existingPattern.TimeToIssue = TimeSpan.FromTicks(
                        (existingPattern.TimeToIssue.Ticks * (existingPattern.OccurrenceCount - 1) +
                         TimeSpan.FromMinutes(5).Ticks) / existingPattern.OccurrenceCount);
                    
                    // Update pattern features
                    existingPattern.Derivatives = derivatives;
                    existingPattern.Correlations = correlations;
                }
                else
                {
                    _degradationPatterns[metric].Add(newPattern);
                }

                // Update hierarchical clustering
                await UpdatePatternClusters(metric);
            }

            await AnalyzePrecursorPatterns(metric, currentValue);
        }

        private double CalculatePatternStability(Pattern pattern)
        {
            if (pattern.Mutations.Count == 0) return 1.0;

            // Calculate average mutation magnitude over time
            var recentMutations = pattern.Mutations
                .OrderByDescending(m => m.Timestamp)
                .Take(10)
                .ToList();

            var avgMagnitude = recentMutations.Average(m => m.MutationMagnitude);
            return Math.Max(0.1, 1.0 - avgMagnitude);
        }

        private async Task UpdatePatternClusters(string metric)
        {
            var patterns = _degradationPatterns[metric];
            if (patterns.Count < 2) return;

            // Calculate distance matrix between all patterns
            var distances = new Dictionary<(Pattern, Pattern), double>();
            for (int i = 0; i < patterns.Count; i++)
            {
                for (int j = i + 1; j < patterns.Count; j++)
                {
                    var distance = CalculatePatternDistance(patterns[i], patterns[j]);
                    distances[(patterns[i], patterns[j])] = distance;
                    distances[(patterns[j], patterns[i])] = distance;
                }
            }

            // Perform hierarchical clustering
            var clusters = patterns.Select(p => new PatternCluster
            {
                Patterns = new List<Pattern> { p },
                Centroid = p,
                Level = 0,
                Confidence = p.Stability
            }).ToList();

            while (clusters.Count > 1)
            {
                // Find closest clusters
                var minDistance = double.MaxValue;
                PatternCluster cluster1 = null, cluster2 = null;

                foreach (var c1 in clusters)
                {
                    foreach (var c2 in clusters)
                    {
                        if (c1 == c2) continue;
                        var distance = CalculateClusterDistance(c1, c2, distances);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            cluster1 = c1;
                            cluster2 = c2;
                        }
                    }
                }

                // Merge clusters
                if (cluster1 != null && cluster2 != null)
                {
                    var newCluster = new PatternCluster
                    {
                        Patterns = cluster1.Patterns.Concat(cluster2.Patterns).ToList(),
                        Children = new List<PatternCluster> { cluster1, cluster2 },
                        Level = Math.Max(cluster1.Level, cluster2.Level) + 1,
                        Confidence = (cluster1.Confidence + cluster2.Confidence) / 2
                    };

                    // Set centroid as the pattern closest to cluster center
                    newCluster.Centroid = CalculateClusterCentroid(newCluster.Patterns);
                    
                    // Set parent references
                    cluster1.Parent = newCluster;
                    cluster2.Parent = newCluster;

                    clusters.Remove(cluster1);
                    clusters.Remove(cluster2);
                    clusters.Add(newCluster);
                }
                else
                {
                    break;
                }
            }

            // Update pattern cluster references
            foreach (var pattern in patterns)
            {
                pattern.Cluster = FindLowestClusterForPattern(pattern, clusters.First());
            }
        }

        private double CalculatePatternDistance(Pattern p1, Pattern p2)
        {
            var sequenceDistance = CalculateDTW(p1.Sequence, p2.Sequence);
            var derivativeDistance = CalculateDTW(p1.Derivatives, p2.Derivatives);
            var correlationDistance = CalculateCorrelationDistance(p1.Correlations, p2.Correlations);

            return (sequenceDistance * 0.5) + (derivativeDistance * 0.3) + (correlationDistance * 0.2);
        }

        private double CalculateCorrelationDistance(Dictionary<string, double> corr1, Dictionary<string, double> corr2)
        {
            var allMetrics = corr1.Keys.Union(corr2.Keys).ToList();
            var sum = 0.0;
            foreach (var metric in allMetrics)
            {
                var v1 = corr1.GetValueOrDefault(metric, 0);
                var v2 = corr2.GetValueOrDefault(metric, 0);
                sum += Math.Pow(v1 - v2, 2);
            }
            return Math.Sqrt(sum / allMetrics.Count);
        }

        private double CalculateClusterDistance(PatternCluster c1, PatternCluster c2, Dictionary<(Pattern, Pattern), double> distances)
        {
            var totalDistance = 0.0;
            var count = 0;

            foreach (var p1 in c1.Patterns)
            {
                foreach (var p2 in c2.Patterns)
                {
                    totalDistance += distances[(p1, p2)];
                    count++;
                }
            }

            return totalDistance / count;
        }

        private Pattern CalculateClusterCentroid(List<Pattern> patterns)
        {
            if (patterns.Count == 1) return patterns[0];

            // Find the pattern with minimum average distance to all other patterns
            var minAvgDistance = double.MaxValue;
            Pattern centroid = null;

            foreach (var pattern in patterns)
            {
                var avgDistance = patterns
                    .Where(p => p != pattern)
                    .Average(p => CalculatePatternDistance(pattern, p));

                if (avgDistance < minAvgDistance)
                {
                    minAvgDistance = avgDistance;
                    centroid = pattern;
                }
            }

            return centroid;
        }

        private PatternCluster FindLowestClusterForPattern(Pattern pattern, PatternCluster root)
        {
            if (root.Children == null || root.Children.Count == 0)
            {
                return root;
            }

            foreach (var child in root.Children)
            {
                if (child.Patterns.Contains(pattern))
                {
                    return FindLowestClusterForPattern(pattern, child);
                }
            }

            return root;
        }

        private double CalculatePatternDifference(List<double> oldPattern, List<double> newPattern)
        {
            return CalculateDTW(oldPattern, newPattern);
        }

        private string DetermineMutationType(List<double> oldPattern, List<double> newPattern)
        {
            var oldTrend = oldPattern.Last() - oldPattern.First();
            var newTrend = newPattern.Last() - newPattern.First();
            var magnitudeChange = Math.Abs(newPattern.Average() - oldPattern.Average());

            if (Math.Sign(oldTrend) != Math.Sign(newTrend))
                return "TrendReversal";
            if (magnitudeChange > 0.5)
                return "MagnitudeShift";
            if (Math.Abs(newTrend - oldTrend) > 0.3)
                return "TrendChange";
            return "MinorVariation";
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
            if (pattern1 == null || pattern2 == null) return false;
            
            // Calculate multiple similarity metrics
            var dtwDistance = CalculateDTW(pattern1, pattern2);
            var shapeDistance = CalculateShapeSimilarity(pattern1, pattern2);
            var trendDistance = CalculateTrendSimilarity(pattern1, pattern2);
            
            // Weighted combination of metrics
            var weightedDistance = (dtwDistance * 0.5) + (shapeDistance * 0.3) + (trendDistance * 0.2);
            
            // Dynamic threshold based on pattern lengths
            var threshold = 0.3 * Math.Max(pattern1.Count, pattern2.Count) / 10.0;
            
            return weightedDistance < threshold;
        }

        private double CalculateDTW(List<double> sequence1, List<double> sequence2)
        {
            int n = sequence1.Count;
            int m = sequence2.Count;
            var dtw = new double[n + 1, m + 1];

            // Initialize DTW matrix
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= m; j++)
                    dtw[i, j] = double.PositiveInfinity;
            dtw[0, 0] = 0;

            // Fill DTW matrix
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    var cost = Math.Abs(sequence1[i - 1] - sequence2[j - 1]);
                    dtw[i, j] = cost + Math.Min(
                        Math.Min(dtw[i - 1, j], dtw[i, j - 1]),
                        dtw[i - 1, j - 1]);
                }
            }

            return dtw[n, m] / Math.Max(n, m); // Normalize by max length
        }

        private double CalculateShapeSimilarity(List<double> sequence1, List<double> sequence2)
        {
            var derivatives1 = CalculateDerivatives(sequence1);
            var derivatives2 = CalculateDerivatives(sequence2);
            
            // Compare shapes using second derivatives
            var secondDerivatives1 = CalculateDerivatives(derivatives1);
            var secondDerivatives2 = CalculateDerivatives(derivatives2);
            
            return CalculatePearsonCorrelation(secondDerivatives1, secondDerivatives2);
        }

        private double CalculateTrendSimilarity(List<double> sequence1, List<double> sequence2)
        {
            var trend1 = sequence1.Last() - sequence1.First();
            var trend2 = sequence2.Last() - sequence2.First();
            
            // Normalize trends
            var maxTrend = Math.Max(Math.Abs(trend1), Math.Abs(trend2));
            if (maxTrend == 0) return 1.0; // Both sequences are flat
            
            return 1.0 - Math.Abs(trend1 - trend2) / maxTrend;
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

        private double CalculateStatisticalConfidence(List<double> actual, List<double> x, double slope, double intercept)
        {
            var predicted = x.Select(xi => slope * xi + intercept);
            var errors = actual.Zip(predicted, (a, p) => Math.Pow(a - p, 2));
            var mse = errors.Average();
            return Math.Exp(-mse); // Transform MSE to confidence score between 0 and 1
        }

        private async Task UpdateModelsIfNeeded()
        {
            if (DateTime.UtcNow - _lastModelTraining > _modelTrainingInterval)
            {
                await _modelTrainingService.TrainModels();
                foreach (var metricType in new[] { "CacheHitRate", "MemoryUsage", "AverageResponseTime" })
                {
                    var model = await _modelTrainingService.GetLatestModel(metricType);
                    if (model != null && await _modelTrainingService.ValidateModel(model, metricType))
                    {
                        _activeModels[metricType] = model;
                    }
                }
                _lastModelTraining = DateTime.UtcNow;
            }
        }

        private float[] ExtractPredictionFeatures(List<double> history)
        {
            var features = new List<float>();
            
            // Historical values
            features.AddRange(history.TakeLast(10).Select(x => (float)x));
            
            // Rate of change
            for (int i = 1; i < history.Count; i++)
            {
                features.Add((float)(history[i] - history[i-1]));
            }
            
            // Statistical features
            features.Add((float)history.Average());
            features.Add((float)history.StdDev());
            features.Add((float)history.Min());
            features.Add((float)history.Max());
            
            return features.ToArray();
        }

        private PredictionResult PredictUsingModel(MLModel model, float[] features)
        {
            var mlContext = new MLContext(seed: 1);
            var predictionEngine = mlContext.Model.CreatePredictionEngine<MetricDataPoint, MetricPrediction>(
                model.Transformer);
            
            var prediction = predictionEngine.Predict(new MetricDataPoint { Features = features });
            
            return new PredictionResult
            {
                Value = prediction.PredictedValue,
                Confidence = prediction.Confidence
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
                    var prediction = await CalculatePrediction(history, metric);
                    predictions[metric] = prediction;
                    
                    // Apply automatic optimization based on predictions
                    await _cacheOptimizer.OptimizeAsync(prediction);
                }
            }

            // Apply optimizations based on trends
            var trends = await AnalyzeTrends();
            foreach (var (metric, trend) in trends)
            {
                await _cacheOptimizer.AdjustEvictionPolicyAsync(trend);
            }

            // Update warning thresholds based on predictions
            var alerts = await GetPredictedAlerts();
            await _cacheOptimizer.UpdateCacheWarningThresholdsAsync(alerts);

            // Optimize cache for frequently accessed items
            if (_metricHistory.ContainsKey("CacheHitRate"))
            {
                var hitRates = _metricHistory
                    .ToDictionary(kv => kv.Key, kv => kv.Value.Last());
                await _cacheOptimizer.PrewarmFrequentItemsAsync(hitRates);
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

        private async Task<PredictionResult> CalculatePrediction(List<double> history, string metric)
        {
            await UpdateModelsIfNeeded();

            var recentPattern = history.Skip(history.Count - _patternWindow).ToList();
            var normalizedRecent = NormalizeSequence(recentPattern);
            var derivatives = CalculateDerivatives(recentPattern);
            var correlations = CalculateCorrelations(metric, _metricHistory.Keys.ToList());

            // Find patterns in the hierarchy that match current behavior
            var hierarchicalMatches = new List<(Pattern Pattern, double Similarity, PatternCluster Cluster)>();
            
            if (_degradationPatterns.ContainsKey(metric))
            {
                foreach (var pattern in _degradationPatterns[metric])
                {
                    var sequenceSimilarity = IsSimilarPattern(pattern.Sequence, normalizedRecent) ? 1.0 : 0.0;
                    if (sequenceSimilarity > 0)
                    {
                        var derivativeSimilarity = CalculatePearsonCorrelation(pattern.Derivatives, derivatives);
                        var correlationSimilarity = CalculateCorrelationSimilarity(pattern.Correlations, correlations);
                        
                        var overallSimilarity = (sequenceSimilarity * 0.5) + 
                                              (derivativeSimilarity * 0.3) + 
                                              (correlationSimilarity * 0.2);

                        hierarchicalMatches.Add((pattern, overallSimilarity, pattern.Cluster));
                    }
                }
            }

            double mlPrediction = 0;
            double mlConfidence = 0;
            
            // Get ML model prediction if available
            if (_activeModels.TryGetValue(metric, out var mlModel))
            {
                var features = ExtractPredictionFeatures(history);
                var prediction = PredictUsingModel(mlModel, features);
                mlPrediction = prediction.Value;
                mlConfidence = prediction.Confidence;
            }

            // If we have hierarchical pattern matches
            if (hierarchicalMatches.Any())
            {
                var bestMatch = hierarchicalMatches.OrderByDescending(m => m.Similarity).First();
                var pattern = bestMatch.Pattern;
                var cluster = bestMatch.Cluster;
                
                // Calculate confidence based on pattern stability and cluster confidence
                var patternConfidence = pattern.Stability * cluster.Confidence * bestMatch.Similarity;
                var timeToImpact = pattern.TimeToIssue;
                
                // Calculate weighted prediction using all similar patterns in the cluster
                var clusterPrediction = cluster.Patterns
                    .Select(p => (GetPatternBasedPrediction(history.Last(), p), p.Stability))
                    .Aggregate(
                        (sum: 0.0, weight: 0.0),
                        (acc, curr) => (acc.sum + curr.Item1 * curr.Item2, acc.weight + curr.Item2));
                
                var patternPrediction = clusterPrediction.sum / clusterPrediction.weight;

                // Blend predictions based on confidence
                if (mlConfidence > 0)
                {
                    var totalConfidence = mlConfidence + patternConfidence;
                    var blendedPrediction = (mlPrediction * mlConfidence +
                        patternPrediction * patternConfidence) / totalConfidence;
                    var blendedConfidence = Math.Max(mlConfidence, patternConfidence);

                    return new PredictionResult
                    {
                        Value = blendedPrediction,
                        Confidence = blendedConfidence,
                        TimeToImpact = timeToImpact,
                        DetectedPattern = new PredictionPattern
                        {
                            Type = pattern.IssueType,
                            Severity = pattern.Severity,
                            Confidence = patternConfidence,
                            TimeToIssue = timeToImpact
                        }
                    };
                }
                else
                {
                    return new PredictionResult
                    {
                        Value = patternPrediction,
                        Confidence = patternConfidence,
                        TimeToImpact = timeToImpact,
                        DetectedPattern = new PredictionPattern
                        {
                            Type = pattern.IssueType,
                            Severity = pattern.Severity,
                            Confidence = patternConfidence,
                            TimeToIssue = timeToImpact
                        }
                    };
                }
            }
            
            // If we have ML prediction with good confidence, use it
            if (mlConfidence > 0.7)
            {
                return new PredictionResult
                {
                    Value = mlPrediction,
                    Confidence = mlConfidence,
                    TimeToImpact = EstimateTimeToImpact(0, history.Last(), mlPrediction)
                };
            }

            // Fallback to statistical prediction
            return CalculateStatisticalPrediction(history);
        }

        private double CalculateCorrelationSimilarity(Dictionary<string, double> corr1, Dictionary<string, double> corr2)
        {
            var allMetrics = corr1.Keys.Union(corr2.Keys).ToList();
            var similarities = new List<double>();

            foreach (var metric in allMetrics)
            {
                if (corr1.TryGetValue(metric, out var v1) && corr2.TryGetValue(metric, out var v2))
                {
                    similarities.Add(1.0 - Math.Abs(v1 - v2));
                }
            }

            return similarities.Any() ? similarities.Average() : 0.0;
        }

        private PredictionResult CalculateStatisticalPrediction(List<double> history)
        {
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
            var confidence = CalculateStatisticalConfidence(history, x, slope, intercept);
            var timeToImpact = EstimateTimeToImpact(slope, history.Last(), regressionPrediction);

            return new PredictionResult
            {
                Value = regressionPrediction,
                Confidence = confidence,
                TimeToImpact = timeToImpact
            };
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