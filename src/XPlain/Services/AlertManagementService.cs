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
        private readonly INotificationService _notificationService;
        private readonly AlertSettings _alertSettings;
        private readonly List<Alert> _alerts = new();
        private readonly object _alertsLock = new();

        public AlertManagementService(
            ILogger<AlertManagementService> logger,
            INotificationService notificationService,
            IOptions<AlertSettings> alertSettings)
        {
            _logger = logger;
            _notificationService = notificationService;
            _alertSettings = alertSettings.Value;
        }

        public Task<List<Alert>> GetActiveAlertsAsync()
        {
            lock (_alertsLock)
            {
                // Return only non-resolved alerts
                return Task.FromResult(_alerts
                    .Where(a => a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Closed)
                    .OrderByDescending(a => a.Severity)
                    .ThenByDescending(a => a.Timestamp)
                    .ToList());
            }
        }

        public Task<List<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate)
        {
            lock (_alertsLock)
            {
                return Task.FromResult(_alerts
                    .Where(a => a.Timestamp >= startDate && a.Timestamp <= endDate)
                    .OrderByDescending(a => a.Timestamp)
                    .ToList());
            }
        }

        public Task<bool> AcknowledgeAlertAsync(string alertId)
        {
            lock (_alertsLock)
            {
                var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
                if (alert == null)
                {
                    _logger.LogWarning($"Attempted to acknowledge non-existent alert: {alertId}");
                    return Task.FromResult(false);
                }

                if (alert.Status != AlertStatus.New)
                {
                    _logger.LogWarning($"Attempted to acknowledge alert that is not in New status: {alertId}");
                    return Task.FromResult(false);
                }

                alert.Status = AlertStatus.Acknowledged;
                alert.AcknowledgedAt = DateTime.UtcNow;
                _logger.LogInformation($"Alert acknowledged: {alertId}");
                
                return Task.FromResult(true);
            }
        }

        public Task<bool> ResolveAlertAsync(string alertId)
        {
            lock (_alertsLock)
            {
                var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
                if (alert == null)
                {
                    _logger.LogWarning($"Attempted to resolve non-existent alert: {alertId}");
                    return Task.FromResult(false);
                }

                if (alert.Status == AlertStatus.Resolved || alert.Status == AlertStatus.Closed)
                {
                    _logger.LogWarning($"Attempted to resolve alert that is already resolved or closed: {alertId}");
                    return Task.FromResult(false);
                }

                alert.Status = AlertStatus.Resolved;
                alert.ResolvedAt = DateTime.UtcNow;
                _logger.LogInformation($"Alert resolved: {alertId}");
                
                return Task.FromResult(true);
            }
        }

        public async Task<string> CreateAlertAsync(Alert alert)
        {
            if (alert == null)
            {
                throw new ArgumentNullException(nameof(alert));
            }

            if (string.IsNullOrEmpty(alert.Id))
            {
                alert.Id = Guid.NewGuid().ToString();
            }
            
            alert.Timestamp = DateTime.UtcNow;
            
            lock (_alertsLock)
            {
                _alerts.Add(alert);
            }
            
            _logger.LogInformation($"Alert created: {alert.Id} - {alert.Title} ({alert.Severity})");
            
            // Send notifications based on severity and settings
            if (_alertSettings.Notifications.EmailEnabled && alert.Severity >= AlertSeverity.High)
            {
                await _notificationService.SendEmailAlertAsync(alert);
            }
            
            if (_alertSettings.Notifications.SlackEnabled && alert.Severity >= AlertSeverity.Medium)
            {
                await _notificationService.SendSlackAlertAsync(alert);
            }
            
            return alert.Id;
        }
    }
}