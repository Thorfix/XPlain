using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class Alert
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public AlertSeverity Severity { get; set; }
        public AlertStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string Source { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    public enum AlertStatus
    {
        New,
        Acknowledged,
        Resolved
    }

    public interface IAlertManagementService
    {
        Task CreateAlertAsync(Alert alert);
        Task AcknowledgeAlertAsync(string alertId);
        Task ResolveAlertAsync(string alertId);
        Task<IEnumerable<Alert>> GetActiveAlertsAsync();
        Task<IEnumerable<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate);
    }

    public class AlertManagementService : IAlertManagementService
    {
        private readonly ConcurrentDictionary<string, Alert> _activeAlerts;
        private readonly List<Alert> _alertHistory;
        private readonly INotificationService _notificationService;

        public AlertManagementService(INotificationService notificationService)
        {
            _activeAlerts = new ConcurrentDictionary<string, Alert>();
            _alertHistory = new List<Alert>();
            _notificationService = notificationService;
        }

        public async Task CreateAlertAsync(Alert alert)
        {
            alert.Id = Guid.NewGuid().ToString();
            alert.CreatedAt = DateTime.UtcNow;
            alert.Status = AlertStatus.New;

            if (_activeAlerts.TryAdd(alert.Id, alert))
            {
                await NotifyAlert(alert);
            }
        }

        public async Task AcknowledgeAlertAsync(string alertId)
        {
            if (_activeAlerts.TryGetValue(alertId, out var alert))
            {
                alert.Status = AlertStatus.Acknowledged;
                alert.AcknowledgedAt = DateTime.UtcNow;
            }
        }

        public async Task ResolveAlertAsync(string alertId)
        {
            if (_activeAlerts.TryRemove(alertId, out var alert))
            {
                alert.Status = AlertStatus.Resolved;
                alert.ResolvedAt = DateTime.UtcNow;
                lock (_alertHistory)
                {
                    _alertHistory.Add(alert);
                }
            }
        }

        public Task<IEnumerable<Alert>> GetActiveAlertsAsync()
        {
            return Task.FromResult(_activeAlerts.Values.AsEnumerable());
        }

        public Task<IEnumerable<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate)
        {
            return Task.FromResult(
                _alertHistory.Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
            );
        }

        private async Task NotifyAlert(Alert alert)
        {
            await _notificationService.SendNotificationAsync(alert);
        }
    }
}