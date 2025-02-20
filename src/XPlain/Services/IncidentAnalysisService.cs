using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public interface IIncidentAnalysisService
    {
        Task<IncidentAnalysisReport> GenerateAnalysisReport();
        Task<List<IncidentPattern>> IdentifyPatterns();
        Task<SystemMetrics> CalculateSystemMetrics();
        Task<List<MitigationRecommendation>> GenerateRecommendations();
        Task TrackMitigationEffectiveness(string mitigationId);
    }

    public class IncidentAnalysisService : IIncidentAnalysisService
    {
        private readonly ILogger<IncidentAnalysisService> _logger;
        private readonly IAutomaticMitigationService _mitigationService;
        private readonly ModelPerformanceMonitor _performanceMonitor;

        public IncidentAnalysisService(
            ILogger<IncidentAnalysisService> logger,
            IAutomaticMitigationService mitigationService,
            ModelPerformanceMonitor performanceMonitor)
        {
            _logger = logger;
            _mitigationService = mitigationService;
            _performanceMonitor = performanceMonitor;
        }

        public async Task<IncidentAnalysisReport> GenerateAnalysisReport()
        {
            try
            {
                var incidents = await _mitigationService.GetIncidentHistory();
                var patterns = await IdentifyPatterns();
                var metrics = await CalculateSystemMetrics();
                var recommendations = await GenerateRecommendations();

                return new IncidentAnalysisReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    TotalIncidents = incidents.Count,
                    Patterns = patterns,
                    SystemMetrics = metrics,
                    Recommendations = recommendations,
                    RecentIncidents = incidents.OrderByDescending(i => i.Timestamp).Take(10).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating incident analysis report");
                throw;
            }
        }

        public async Task<List<IncidentPattern>> IdentifyPatterns()
        {
            var incidents = await _mitigationService.GetIncidentHistory();
            var patterns = new List<IncidentPattern>();

            // Group incidents by similar characteristics
            var groupedIncidents = incidents
                .GroupBy(i => new 
                { 
                    Category = i.Metadata.GetValueOrDefault("category"),
                    Severity = i.Metadata.GetValueOrDefault("severity")
                });

            foreach (var group in groupedIncidents)
            {
                if (group.Count() >= 3) // Pattern threshold
                {
                    patterns.Add(new IncidentPattern
                    {
                        Category = group.Key.Category?.ToString(),
                        Frequency = group.Count(),
                        LastOccurrence = group.Max(i => i.Timestamp),
                        AvgRecoveryTime = CalculateAverageRecoveryTime(group.ToList()),
                        RelatedIncidents = group.ToList()
                    });
                }
            }

            return patterns;
        }

        public async Task<SystemMetrics> CalculateSystemMetrics()
        {
            var incidents = await _mitigationService.GetIncidentHistory();
            var orderedIncidents = incidents.OrderBy(i => i.Timestamp).ToList();

            var mtbf = CalculateMTBF(orderedIncidents);
            var mttr = CalculateMTTR(orderedIncidents);
            var availabilityPercentage = CalculateAvailability(mtbf, mttr);

            return new SystemMetrics
            {
                MTBF = mtbf,
                MTTR = mttr,
                Availability = availabilityPercentage,
                SuccessfulMitigations = incidents.Count(i => 
                    i.Metadata.GetValueOrDefault("status")?.ToString() == "success"),
                TotalIncidents = incidents.Count
            };
        }

        public async Task<List<MitigationRecommendation>> GenerateRecommendations()
        {
            var patterns = await IdentifyPatterns();
            var recommendations = new List<MitigationRecommendation>();

            foreach (var pattern in patterns)
            {
                var recommendation = new MitigationRecommendation
                {
                    Pattern = pattern,
                    Priority = CalculatePriority(pattern),
                    SuggestedActions = GenerateSuggestedActions(pattern),
                    EstimatedImpact = EstimateImpact(pattern)
                };
                recommendations.Add(recommendation);
            }

            return recommendations.OrderByDescending(r => r.Priority).ToList();
        }

        public async Task TrackMitigationEffectiveness(string mitigationId)
        {
            // Track the effectiveness of applied mitigations over time
            var incidents = await _mitigationService.GetIncidentHistory();
            var relatedIncidents = incidents.Where(i => 
                i.Metadata.GetValueOrDefault("mitigationId")?.ToString() == mitigationId).ToList();

            var effectiveness = CalculateMitigationEffectiveness(relatedIncidents);
            _logger.LogInformation("Mitigation {MitigationId} effectiveness: {Effectiveness}%", 
                mitigationId, effectiveness);
        }

        private TimeSpan CalculateAverageRecoveryTime(List<MitigationIncident> incidents)
        {
            var recoveryTimes = incidents
                .Where(i => i.Metadata.ContainsKey("recoveryTime"))
                .Select(i => TimeSpan.Parse(i.Metadata["recoveryTime"].ToString()));

            return recoveryTimes.Any() 
                ? TimeSpan.FromTicks((long)recoveryTimes.Average(t => t.Ticks))
                : TimeSpan.Zero;
        }

        private TimeSpan CalculateMTBF(List<MitigationIncident> incidents)
        {
            if (incidents.Count < 2) return TimeSpan.Zero;

            var totalTime = incidents.Last().Timestamp - incidents.First().Timestamp;
            return TimeSpan.FromTicks(totalTime.Ticks / (incidents.Count - 1));
        }

        private TimeSpan CalculateMTTR(List<MitigationIncident> incidents)
        {
            var recoveryTimes = incidents
                .Where(i => i.Metadata.ContainsKey("recoveryTime"))
                .Select(i => TimeSpan.Parse(i.Metadata["recoveryTime"].ToString()));

            return recoveryTimes.Any()
                ? TimeSpan.FromTicks((long)recoveryTimes.Average(t => t.Ticks))
                : TimeSpan.Zero;
        }

        private double CalculateAvailability(TimeSpan mtbf, TimeSpan mttr)
        {
            if (mtbf == TimeSpan.Zero || mttr == TimeSpan.Zero) return 100;

            var totalTime = mtbf + mttr;
            return (mtbf.TotalMinutes / totalTime.TotalMinutes) * 100;
        }

        private int CalculatePriority(IncidentPattern pattern)
        {
            int priority = 0;
            
            priority += pattern.Frequency * 2; // More frequent = higher priority
            priority += pattern.RelatedIncidents.Count(i => 
                i.Metadata.GetValueOrDefault("severity")?.ToString() == "high") * 3;
            
            return Math.Min(priority, 10); // Scale 0-10
        }

        private List<string> GenerateSuggestedActions(IncidentPattern pattern)
        {
            var actions = new List<string>();
            
            if (pattern.AvgRecoveryTime > TimeSpan.FromMinutes(10))
            {
                actions.Add("Implement faster rollback mechanism");
            }
            
            if (pattern.Frequency > 5)
            {
                actions.Add("Review and update validation thresholds");
                actions.Add("Implement additional pre-deployment checks");
            }

            return actions;
        }

        private double EstimateImpact(IncidentPattern pattern)
        {
            double impact = 0;
            
            // Calculate impact based on frequency and severity
            impact += pattern.Frequency * 0.3;
            impact += pattern.RelatedIncidents.Count(i => 
                i.Metadata.GetValueOrDefault("severity")?.ToString() == "high") * 0.5;
            
            return Math.Min(impact, 1.0); // Scale 0-1
        }

        private double CalculateMitigationEffectiveness(List<MitigationIncident> incidents)
        {
            if (!incidents.Any()) return 0;

            var successful = incidents.Count(i => 
                i.Metadata.GetValueOrDefault("status")?.ToString() == "success");
            
            return (double)successful / incidents.Count * 100;
        }
    }

    public class IncidentAnalysisReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalIncidents { get; set; }
        public List<IncidentPattern> Patterns { get; set; }
        public SystemMetrics SystemMetrics { get; set; }
        public List<MitigationRecommendation> Recommendations { get; set; }
        public List<MitigationIncident> RecentIncidents { get; set; }
    }

    public class IncidentPattern
    {
        public string Category { get; set; }
        public int Frequency { get; set; }
        public DateTime LastOccurrence { get; set; }
        public TimeSpan AvgRecoveryTime { get; set; }
        public List<MitigationIncident> RelatedIncidents { get; set; }
    }

    public class SystemMetrics
    {
        public TimeSpan MTBF { get; set; }
        public TimeSpan MTTR { get; set; }
        public double Availability { get; set; }
        public int SuccessfulMitigations { get; set; }
        public int TotalIncidents { get; set; }
    }

    public class MitigationRecommendation
    {
        public IncidentPattern Pattern { get; set; }
        public int Priority { get; set; }
        public List<string> SuggestedActions { get; set; }
        public double EstimatedImpact { get; set; }
    }
}