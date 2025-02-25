using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class AlertManagementService : IAlertManagementService
    {
        private readonly ILogger<AlertManagementService> _logger;
        private readonly List<Alert> _alerts = new();
        private readonly INotificationService _notificationService;
        private readonly AlertSettings _settings;

        public AlertManagementService(
            ILogger<AlertManagementService> logger = null,
            INotificationService notificationService = null,
            IOptions<AlertSettings> settings = null)
        {
            _logger = logger ?? new Logger<AlertManagementService>(new LoggerFactory());
            _notificationService = notificationService;
            _settings = settings?.Value ?? new AlertSettings();
        }

        public async Task<List<Alert>> GetActiveAlertsAsync()
        {
            return _alerts
                .Where(a => a.ResolvedAt == null)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();
        }

        public async Task<List<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate)
        {
            return _alerts
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();
        }

        public async Task<bool> AcknowledgeAlertAsync(string alertId)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null)
            {
                return false;
            }

            if (alert.AcknowledgedAt == null)
            {
                alert.AcknowledgedAt = DateTime.UtcNow;
                _logger.LogInformation($"Alert acknowledged: {alert.Type} - {alert.Message}");
                return true;
            }

            return false;
        }

        public async Task<bool> ResolveAlertAsync(string alertId)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null)
            {
                return false;
            }

            if (alert.ResolvedAt == null)
            {
                alert.ResolvedAt = DateTime.UtcNow;
                _logger.LogInformation($"Alert resolved: {alert.Type} - {alert.Message}");
                return true;
            }

            return false;
        }

        public async Task<string> CreateAlertAsync(string type, string message, string severity, Dictionary<string, object> metadata = null)
        {
            var alert = new Alert
            {
                Type = type,
                Message = message,
                Severity = severity,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            _alerts.Add(alert);
            _logger.LogInformation($"Alert created: {severity} - {type} - {message}");

            // Send notification for critical and error alerts
            if (_notificationService != null && (severity == "Critical" || severity == "Error"))
            {
                await _notificationService.SendAlertNotificationAsync(alert);
            }

            // Limit alert history size
            if (_alerts.Count > _settings.MaxAlertHistorySize)
            {
                var oldestResolved = _alerts
                    .Where(a => a.ResolvedAt != null)
                    .OrderBy(a => a.ResolvedAt)
                    .FirstOrDefault();

                if (oldestResolved != null)
                {
                    _alerts.Remove(oldestResolved);
                }
            }

            return alert.Id;
        }
    }
}