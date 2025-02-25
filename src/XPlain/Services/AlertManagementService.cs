using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public interface IAlertManagementService
    {
        Task<List<CacheAlert>> GetActiveAlertsAsync();
        Task<List<CacheAlert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate);
        Task<bool> AcknowledgeAlertAsync(string alertId);
        Task<bool> ResolveAlertAsync(string alertId);
    }

    public class AlertManagementService : IAlertManagementService
    {
        private readonly ILogger<AlertManagementService> _logger;
        private readonly List<CacheAlert> _activeAlerts = new();
        private readonly List<CacheAlert> _alertHistory = new();
        private readonly INotificationService _notificationService;
        private readonly AlertSettings _settings;

        public AlertManagementService(
            ILogger<AlertManagementService> logger = null,
            INotificationService notificationService = null,
            IOptions<AlertSettings> settings = null)
        {
            _logger = logger ?? new Logger<AlertManagementService>(new LoggerFactory());
            _notificationService = notificationService ?? new NotificationService();
            _settings = settings?.Value ?? new AlertSettings();
        }

        public async Task<List<CacheAlert>> GetActiveAlertsAsync()
        {
            return _activeAlerts;
        }

        public async Task<List<CacheAlert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate)
        {
            return _alertHistory
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();
        }

        public async Task<bool> AcknowledgeAlertAsync(string alertId)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null)
                return false;
                
            // Add acknowledged flag to metadata
            if (alert.Metadata == null)
                alert.Metadata = new Dictionary<string, object>();
                
            alert.Metadata["acknowledged"] = true;
            alert.Metadata["acknowledged_at"] = DateTime.UtcNow;
            
            _logger.LogInformation($"Alert {alertId} ({alert.Type}) acknowledged");
            
            return true;
        }

        public async Task<bool> ResolveAlertAsync(string alertId)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null)
                return false;
                
            // Remove from active alerts
            _activeAlerts.Remove(alert);
            
            // Add to history
            if (!_alertHistory.Any(a => a.Id == alertId))
            {
                _alertHistory.Add(alert);
            }
            
            // Add resolved flag to metadata
            if (alert.Metadata == null)
                alert.Metadata = new Dictionary<string, object>();
                
            alert.Metadata["resolved"] = true;
            alert.Metadata["resolved_at"] = DateTime.UtcNow;
            
            _logger.LogInformation($"Alert {alertId} ({alert.Type}) resolved");
            
            // Limit history size
            while (_alertHistory.Count > 1000)
            {
                _alertHistory.RemoveAt(0);
            }
            
            return true;
        }

        public async Task<bool> CreateAlertAsync(
            string type,
            string message,
            string severity,
            Dictionary<string, object> metadata = null)
        {
            var alert = new CacheAlert
            {
                Type = type,
                Message = message,
                Severity = severity,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
            
            // Check for duplicates within the throttling interval
            var recentDuplicate = _activeAlerts.FirstOrDefault(a => 
                a.Type == type && 
                a.Severity == severity &&
                DateTime.UtcNow - a.CreatedAt < TimeSpan.FromSeconds(_settings.ThrottlingIntervalSeconds));
                
            if (recentDuplicate != null)
            {
                // Update the existing alert instead of creating a new one
                if (_settings.GroupSimilarAlerts)
                {
                    recentDuplicate.Metadata["count"] = (recentDuplicate.Metadata.GetValueOrDefault("count", 1) as int? ?? 1) + 1;
                    recentDuplicate.Metadata["last_occurrence"] = DateTime.UtcNow;
                    return true;
                }
            }
            
            _activeAlerts.Add(alert);
            _logger.LogInformation($"Created alert: [{severity}] {type} - {message}");
            
            // Send notification if configured
            await _notificationService.SendAlertNotificationAsync(alert);
            
            return true;
        }
    }
}