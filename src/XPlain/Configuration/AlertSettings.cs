using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class AlertSettings
    {
        public ModelPerformanceAlertSettings ModelPerformance { get; set; } = new ModelPerformanceAlertSettings();
        public NotificationSettings Notifications { get; set; } = new NotificationSettings();
    }
    
    public class ModelPerformanceAlertSettings
    {
        [Range(0, 1)]
        public double AccuracyThreshold { get; set; } = 0.95;
        
        [Range(0, 1)]
        public double PrecisionThreshold { get; set; } = 0.90;
        
        [Range(0, 1)]
        public double RecallThreshold { get; set; } = 0.90;
        
        [Range(0, 1)]
        public double F1ScoreThreshold { get; set; } = 0.90;
        
        [Range(1, 10)]
        public int ConsecutiveFailuresThreshold { get; set; } = 3;
        
        [Range(0, 0.5)]
        public double DegradationTrendThreshold { get; set; } = 0.05;
    }
    
    public class NotificationSettings
    {
        public bool EmailEnabled { get; set; } = false;
        public string EmailRecipients { get; set; } = "ml-team@example.com";
        public bool SlackEnabled { get; set; } = false;
        public string SlackWebhookUrl { get; set; } = "https://hooks.slack.com/services/your-webhook-url";
    }
}