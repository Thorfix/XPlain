namespace XPlain.Configuration
{
    public class AlertSettings
    {
        public ModelPerformanceAlertThresholds ModelPerformance { get; set; }
        public AlertNotificationSettings Notifications { get; set; }
    }

    public class ModelPerformanceAlertThresholds
    {
        public double AccuracyThreshold { get; set; } = 0.95;
        public double PrecisionThreshold { get; set; } = 0.90;
        public double RecallThreshold { get; set; } = 0.90;
        public double F1ScoreThreshold { get; set; } = 0.90;
        public int ConsecutiveFailuresThreshold { get; set; } = 3;
        public double DegradationTrendThreshold { get; set; } = 0.05;
    }

    public class AlertNotificationSettings
    {
        public bool EmailEnabled { get; set; }
        public string EmailRecipients { get; set; }
        public bool SlackEnabled { get; set; }
        public string SlackWebhookUrl { get; set; }
    }
}