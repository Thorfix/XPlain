namespace XPlain.Configuration
{
    public class MetricsSettings
    {
        public string TimeSeriesConnectionString { get; set; } = "";
        public string DatabaseName { get; set; } = "metrics";
        public int DefaultRetentionDays { get; set; } = 30;
        public int CleanupIntervalMinutes { get; set; } = 60;
        public int QueryFrequencyWindowMinutes { get; set; } = 60;
        public int ResponseTimeWindowMinutes { get; set; } = 5;
        public int HitRateWindowMinutes { get; set; } = 5;
        public int UserActivityWindowMinutes { get; set; } = 15;
    }
}