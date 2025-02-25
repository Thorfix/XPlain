using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class MetricsSettings
    {
        [Required]
        public string TimeSeriesConnectionString { get; set; } = "http://localhost:8086?token=your-token";
        
        [Required]
        public string DatabaseName { get; set; } = "cache_metrics";
        
        [Range(1, 365)]
        public int DefaultRetentionDays { get; set; } = 30;
        
        [Range(1, 1440)]
        public int CleanupIntervalMinutes { get; set; } = 60;
        
        [Range(1, 1440)]
        public int QueryFrequencyWindowMinutes { get; set; } = 60;
        
        [Range(1, 1440)]
        public int ResponseTimeWindowMinutes { get; set; } = 5;
        
        [Range(1, 1440)]
        public int HitRateWindowMinutes { get; set; } = 5;
        
        [Range(1, 1440)]
        public int UserActivityWindowMinutes { get; set; } = 15;
    }
}