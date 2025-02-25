using System;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class AlertSettings
    {
        public bool NotifyOnCritical { get; set; } = true;
        
        public bool NotifyOnError { get; set; } = true;
        
        public bool NotifyOnWarning { get; set; } = false;
        
        public bool NotifyOnInfo { get; set; } = false;
        
        public bool EnableEmailNotifications { get; set; } = false;
        
        public string EmailRecipients { get; set; } = "";
        
        public bool EnableSlackNotifications { get; set; } = false;
        
        public string SlackWebhookUrl { get; set; } = "";
        
        [Range(1, 3600)]
        public int ThrottlingIntervalSeconds { get; set; } = 300;
        
        public bool AutoResolveWarnings { get; set; } = true;
        
        [Range(1, 86400)]
        public int AutoResolveWarningsAfterSeconds { get; set; } = 3600;
        
        public bool GroupSimilarAlerts { get; set; } = true;
        
        public void Validate()
        {
            if (EnableEmailNotifications && string.IsNullOrWhiteSpace(EmailRecipients))
                throw new ValidationException("Email recipients must be specified when email notifications are enabled");
                
            if (EnableSlackNotifications && string.IsNullOrWhiteSpace(SlackWebhookUrl))
                throw new ValidationException("Slack webhook URL must be specified when Slack notifications are enabled");
                
            if (ThrottlingIntervalSeconds < 1 || ThrottlingIntervalSeconds > 3600)
                throw new ValidationException("Throttling interval must be between 1 and 3600 seconds");
                
            if (AutoResolveWarningsAfterSeconds < 1 || AutoResolveWarningsAfterSeconds > 86400)
                throw new ValidationException("Auto-resolve warnings after seconds must be between 1 and 86400");
        }
    }
}