using System;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class MetricsSettings
    {
        [Range(1, 3600)]
        public int CollectionIntervalSeconds { get; set; } = 60;
        
        [Range(1, 1000)]
        public int MaxDataPointsPerMetric { get; set; } = 1000;
        
        [Range(1, 365)]
        public int DefaultRetentionDays { get; set; } = 7;
        
        [Range(10, 10000)]
        public double ResponseTimeAlertThresholdMs { get; set; } = 500;
        
        [Range(0.01, 1.0)]
        public double ErrorRateAlertThreshold { get; set; } = 0.1;
        
        [Range(50, 10000)]
        public double MemoryUsageAlertThresholdMB { get; set; } = 1000;
        
        public bool EnableDetailedMetrics { get; set; } = true;
        
        public bool EnableHistoricalStorage { get; set; } = true;
        
        public void Validate()
        {
            if (CollectionIntervalSeconds < 1 || CollectionIntervalSeconds > 3600)
                throw new ValidationException("Collection interval must be between 1 and 3600 seconds");
                
            if (MaxDataPointsPerMetric < 1 || MaxDataPointsPerMetric > 1000)
                throw new ValidationException("Max data points per metric must be between 1 and 1000");
                
            if (DefaultRetentionDays < 1 || DefaultRetentionDays > 365)
                throw new ValidationException("Default retention days must be between 1 and 365");
                
            if (ResponseTimeAlertThresholdMs < 10 || ResponseTimeAlertThresholdMs > 10000)
                throw new ValidationException("Response time alert threshold must be between 10 and 10000 ms");
                
            if (ErrorRateAlertThreshold < 0.01 || ErrorRateAlertThreshold > 1.0)
                throw new ValidationException("Error rate alert threshold must be between 0.01 and 1.0");
                
            if (MemoryUsageAlertThresholdMB < 50 || MemoryUsageAlertThresholdMB > 10000)
                throw new ValidationException("Memory usage alert threshold must be between 50 and 10000 MB");
        }
    }
}