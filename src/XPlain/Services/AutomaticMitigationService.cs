using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public interface IAutomaticMitigationService
    {
        Task InitiateModelRollback();
        Task<bool> ValidateRollbackSuccess();
        Task LogIncident(string description, Dictionary<string, object> metadata);
        Task<List<MitigationIncident>> GetIncidentHistory();
    }

    public class AutomaticMitigationService : IAutomaticMitigationService
    {
        private readonly ILogger<AutomaticMitigationService> _logger;
        private readonly MLModelValidationService _validationService;
        private readonly MLModelTrainingService _trainingService;
        private readonly List<MitigationIncident> _incidentHistory;
        private string _lastStableModelVersion;

        public AutomaticMitigationService(
            ILogger<AutomaticMitigationService> logger,
            MLModelValidationService validationService,
            MLModelTrainingService trainingService)
        {
            _logger = logger;
            _validationService = validationService;
            _trainingService = trainingService;
            _incidentHistory = new List<MitigationIncident>();
            _lastStableModelVersion = trainingService.GetLatestStableVersion();
        }

        public async Task InitiateModelRollback()
        {
            try
            {
                _logger.LogWarning("Initiating model rollback to last stable version: {Version}", _lastStableModelVersion);
                
                var rollbackSuccess = await _trainingService.RollbackToVersion(_lastStableModelVersion);
                if (rollbackSuccess)
                {
                    await LogIncident("Automatic model rollback performed", new Dictionary<string, object>
                    {
                        { "previous_version", _lastStableModelVersion },
                        { "timestamp", DateTime.UtcNow },
                        { "status", "success" }
                    });
                    
                    _logger.LogInformation("Model rollback completed successfully");
                }
                else
                {
                    _logger.LogError("Model rollback failed");
                    throw new Exception("Failed to rollback to stable version");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during model rollback");
                throw;
            }
        }

        public async Task<bool> ValidateRollbackSuccess()
        {
            try
            {
                var validationResult = await _validationService.ValidateCurrentModel();
                var isValid = validationResult.Accuracy >= 0.95 && validationResult.F1Score >= 0.90;
                
                if (isValid)
                {
                    _logger.LogInformation("Rollback validation successful");
                }
                else
                {
                    _logger.LogWarning("Rollback validation failed. Metrics below threshold");
                }
                
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating rollback");
                return false;
            }
        }

        public async Task LogIncident(string description, Dictionary<string, object> metadata)
        {
            var severity = metadata.ContainsKey("severity") 
                ? Enum.Parse<IncidentSeverity>(metadata["severity"].ToString()) 
                : IncidentSeverity.Medium;

            var performanceMetrics = await _validationService.ValidateCurrentModel();

            var incident = new MitigationIncident
            {
                Timestamp = DateTime.UtcNow,
                Description = description,
                Severity = severity,
                Category = metadata.GetValueOrDefault("category")?.ToString(),
                CorrelationId = metadata.GetValueOrDefault("correlationId")?.ToString(),
                AffectedUsers = metadata.ContainsKey("affectedUsers") 
                    ? Convert.ToInt32(metadata["affectedUsers"]) 
                    : 0,
                PerformanceMetrics = new Dictionary<string, object>
                {
                    { "accuracy", performanceMetrics.Accuracy },
                    { "f1Score", performanceMetrics.F1Score },
                    { "latency", performanceMetrics.AverageLatency }
                },
                Metadata = metadata
            };

            _incidentHistory.Add(incident);
            _logger.LogInformation(
                "Incident logged: {Description} [Severity: {Severity}, Category: {Category}, CorrelationId: {CorrelationId}]",
                description, severity, incident.Category, incident.CorrelationId);
        }

        public async Task<List<MitigationIncident>> GetIncidentHistory()
        {
            return _incidentHistory;
        }
    }

    public enum IncidentSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class MitigationIncident
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; }
        public string Description { get; set; }
        public IncidentSeverity Severity { get; set; }
        public string Category { get; set; }
        public string CorrelationId { get; set; }
        public int AffectedUsers { get; set; }
        public Dictionary<string, object> PerformanceMetrics { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public TimeSpan? RecoveryTime { get; set; }
    }
}