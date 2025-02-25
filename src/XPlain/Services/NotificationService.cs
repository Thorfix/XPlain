using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public interface INotificationService
    {
        Task SendEmailAlertAsync(Alert alert);
        Task SendSlackAlertAsync(Alert alert);
    }

    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly AlertSettings _alertSettings;
        private readonly HttpClient _httpClient;

        public NotificationService(
            ILogger<NotificationService> logger,
            IOptions<AlertSettings> alertSettings,
            HttpClient httpClient)
        {
            _logger = logger;
            _alertSettings = alertSettings.Value;
            _httpClient = httpClient;
        }

        public Task SendEmailAlertAsync(Alert alert)
        {
            if (!_alertSettings.Notifications.EmailEnabled || 
                string.IsNullOrEmpty(_alertSettings.Notifications.EmailRecipients))
            {
                _logger.LogDebug("Email notifications disabled or no recipients configured");
                return Task.CompletedTask;
            }

            // In a real implementation, this would connect to an SMTP server
            // For now, we'll just log the notification
            _logger.LogInformation(
                $"[EMAIL NOTIFICATION] To: {_alertSettings.Notifications.EmailRecipients}, " +
                $"Subject: {FormatAlertSubject(alert)}, " +
                $"Body: {FormatAlertBody(alert)}");

            return Task.CompletedTask;
        }

        public async Task SendSlackAlertAsync(Alert alert)
        {
            if (!_alertSettings.Notifications.SlackEnabled || 
                string.IsNullOrEmpty(_alertSettings.Notifications.SlackWebhookUrl))
            {
                _logger.LogDebug("Slack notifications disabled or no webhook URL configured");
                return;
            }

            try
            {
                var slackMessage = new
                {
                    text = $"*{FormatAlertSubject(alert)}*",
                    blocks = new[]
                    {
                        new
                        {
                            type = "header",
                            text = new
                            {
                                type = "plain_text",
                                text = FormatAlertSubject(alert)
                            }
                        },
                        new
                        {
                            type = "section",
                            text = new
                            {
                                type = "mrkdwn",
                                text = FormatAlertBody(alert)
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(slackMessage);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // In a real implementation, we would send the request to the Slack webhook
                // For now, we'll just log the notification
                _logger.LogInformation($"[SLACK NOTIFICATION] Webhook URL: {_alertSettings.Notifications.SlackWebhookUrl.Substring(0, 20)}..., Message: {json}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Slack notification");
            }
        }

        private string FormatAlertSubject(Alert alert)
        {
            return $"[{alert.Severity}] {alert.Title}";
        }

        private string FormatAlertBody(Alert alert)
        {
            var sb = new StringBuilder();
            sb.AppendLine(alert.Description);
            sb.AppendLine();
            sb.AppendLine($"*ID:* {alert.Id}");
            sb.AppendLine($"*Severity:* {alert.Severity}");
            sb.AppendLine($"*Time:* {alert.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");
            sb.AppendLine($"*Source:* {alert.Source}");
            
            if (alert.Metadata.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("*Additional Information:*");
                foreach (var item in alert.Metadata)
                {
                    sb.AppendLine($"• {item.Key}: {item.Value}");
                }
            }

            return sb.ToString();
        }
    }
}