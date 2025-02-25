using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public interface INotificationService
    {
        Task<bool> SendNotificationAsync(string subject, string message, string severity);
        Task<bool> SendAlertNotificationAsync(CacheAlert alert);
        Task<List<NotificationEvent>> GetNotificationHistoryAsync();
    }

    public class NotificationEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Subject { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool Delivered { get; set; }
        public string Channel { get; set; } = "console";
    }

    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly List<NotificationEvent> _notificationHistory = new();
        private readonly AlertSettings _settings;
        
        public NotificationService(
            ILogger<NotificationService> logger = null,
            IOptions<AlertSettings> settings = null)
        {
            _logger = logger ?? new Logger<NotificationService>(new LoggerFactory());
            _settings = settings?.Value ?? new AlertSettings();
        }

        public async Task<bool> SendNotificationAsync(string subject, string message, string severity)
        {
            _logger.LogInformation($"[{severity}] {subject}: {message}");
            
            var notification = new NotificationEvent
            {
                Subject = subject,
                Message = message,
                Severity = severity,
                Delivered = true
            };
            
            _notificationHistory.Add(notification);
            
            // Limit history size
            while (_notificationHistory.Count > 100)
            {
                _notificationHistory.RemoveAt(0);
            }
            
            return true;
        }

        public async Task<bool> SendAlertNotificationAsync(CacheAlert alert)
        {
            if (string.IsNullOrEmpty(alert?.Message))
                return false;
                
            var shouldNotify = alert.Severity.ToLowerInvariant() switch
            {
                "critical" => true,
                "error" => _settings.NotifyOnError,
                "warning" => _settings.NotifyOnWarning,
                "info" => _settings.NotifyOnInfo,
                _ => false
            };
            
            if (!shouldNotify)
                return false;
                
            return await SendNotificationAsync(
                $"Cache Alert: {alert.Type}",
                alert.Message,
                alert.Severity);
        }

        public async Task<List<NotificationEvent>> GetNotificationHistoryAsync()
        {
            return _notificationHistory;
        }
    }
}