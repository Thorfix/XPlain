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
        Task<string> CreateAlertAsync(string type, string message, string severity, Dictionary<string, object> metadata = null);
    }

    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string Status => ResolvedAt.HasValue ? "Resolved" : 
                                AcknowledgedAt.HasValue ? "Acknowledged" : 
                                "Active";
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}