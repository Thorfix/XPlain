using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public interface IAlertManagementService
    {
        Task<List<Alert>> GetActiveAlertsAsync();
        Task<List<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate);
        Task<bool> AcknowledgeAlertAsync(string alertId);
        Task<bool> ResolveAlertAsync(string alertId);
    }

    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string Source { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class AlertManagementService : IAlertManagementService
    {
        private readonly List<Alert> _alerts = new List<Alert>();
        private readonly AlertSettings _settings;
        private readonly INotificationService _notificationService;
        
        public AlertManagementService(
            IOptions<AlertSettings> settings = null,
            INotificationService notificationService = null)
        {
            _settings = settings?.Value ?? new AlertSettings();
            _notificationService = notificationService ?? new NotificationService();
            
            // Add some sample alerts
            _alerts.Add(new Alert
            {
                Title = "Cache hit rate below threshold",
                Description = "Cache hit rate is 45%, which is below the warning threshold of 60%",
                Severity = "Warning",
                Source = "CacheMonitoring",
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            });
            
            _alerts.Add(new Alert
            {
                Title = "Memory usage high",
                Description = "Memory usage is at 85%, approaching critical threshold",
                Severity = "Warning",
                Source = "ResourceMonitor",
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            });
        }
        
        public Task<List<Alert>> GetActiveAlertsAsync()
        {
            return Task.FromResult(_alerts.Where(a => a.ResolvedAt == null).ToList());
        }
        
        public Task<List<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate)
        {
            return Task.FromResult(_alerts
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
                .OrderByDescending(a => a.CreatedAt)
                .ToList());
        }
        
        public Task<bool> AcknowledgeAlertAsync(string alertId)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null && alert.AcknowledgedAt == null)
            {
                alert.AcknowledgedAt = DateTime.UtcNow;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        
        public Task<bool> ResolveAlertAsync(string alertId)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null && alert.ResolvedAt == null)
            {
                alert.ResolvedAt = DateTime.UtcNow;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }
}