using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IAlertManagementService
    {
        Task<List<Alert>> GetActiveAlertsAsync();
        Task<List<Alert>> GetAlertHistoryAsync(DateTime startDate, DateTime endDate);
        Task<bool> AcknowledgeAlertAsync(string alertId);
        Task<bool> ResolveAlertAsync(string alertId);
        Task<string> CreateAlertAsync(Alert alert);
    }

    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string Description { get; set; }
        public AlertSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public AlertStatus Status { get; set; } = AlertStatus.New;
        public string Source { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string AssignedTo { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum AlertStatus
    {
        New,
        Acknowledged,
        Resolved,
        Closed
    }
}