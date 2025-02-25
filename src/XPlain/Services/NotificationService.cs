using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
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

        public async Task<bool> SendAlertNotificationAsync(Alert alert)
        {
            try
            {
                _logger.LogInformation($"Sending alert notification: {alert.Severity} - {alert.Type} - {alert.Message}");

                // In a real implementation, this would integrate with email/SMS/Slack/etc.
                // Mock implementation for demonstration purposes
                var notification = new NotificationEvent
                {
                    Type = "Alert",
                    TargetId = alert.Id,
                    Success = true,
                    Channel = DetermineNotificationChannel(alert.Severity),
                    Metadata = new Dictionary<string, object>
                    {
                        ["severity"] = alert.Severity,
                        ["type"] = alert.Type,
                        ["message"] = alert.Message
                    }
                };

                _notificationHistory.Add(notification);

                // Simulate network delay
                await Task.Delay(50);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send alert notification: {alert.Id}");

                var failedNotification = new NotificationEvent
                {
                    Type = "Alert",
                    TargetId = alert.Id,
                    Success = false,
                    Channel = DetermineNotificationChannel(alert.Severity),
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }
                };

                _notificationHistory.Add(failedNotification);

                return false;
            }
        }

        public async Task<bool> SendMitigationNotificationAsync(MitigationAction mitigation)
        {
            try
            {
                _logger.LogInformation($"Sending mitigation notification: {mitigation.Type} - {mitigation.Description}");

                // Mock implementation for demonstration purposes
                var notification = new NotificationEvent
                {
                    Type = "Mitigation",
                    TargetId = mitigation.Id,
                    Success = true,
                    Channel = "Email",
                    Metadata = new Dictionary<string, object>
                    {
                        ["type"] = mitigation.Type,
                        ["description"] = mitigation.Description,
                        ["status"] = mitigation.ResultStatus
                    }
                };

                _notificationHistory.Add(notification);

                // Simulate network delay
                await Task.Delay(50);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send mitigation notification: {mitigation.Id}");

                var failedNotification = new NotificationEvent
                {
                    Type = "Mitigation",
                    TargetId = mitigation.Id,
                    Success = false,
                    Channel = "Email",
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }
                };

                _notificationHistory.Add(failedNotification);

                return false;
            }
        }

        public async Task<List<NotificationEvent>> GetNotificationHistoryAsync(TimeSpan period)
        {
            var cutoff = DateTime.UtcNow - period;
            return _notificationHistory
                .Where(n => n.SentAt >= cutoff)
                .OrderByDescending(n => n.SentAt)
                .ToList();
        }

        private string DetermineNotificationChannel(string severity)
        {
            return severity switch
            {
                "Critical" => "SMS,Email,Slack",
                "Error" => "Email,Slack",
                "Warning" => "Email",
                _ => "Email"
            };
        }
    }
}