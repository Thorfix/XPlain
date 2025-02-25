using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface INotificationService
    {
        Task<bool> SendAlertNotificationAsync(Alert alert);
        Task<bool> SendMitigationNotificationAsync(MitigationAction mitigation);
        Task<List<NotificationEvent>> GetNotificationHistoryAsync(TimeSpan period);
    }

    public class NotificationEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; }
        public string TargetId { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; }
        public string Channel { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}