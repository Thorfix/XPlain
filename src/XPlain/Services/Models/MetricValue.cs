using System;

namespace XPlain.Services
{
    /// <summary>
    /// Represents a metric value with timestamp
    /// </summary>
    public class MetricValue
    {
        /// <summary>
        /// The current value of the metric
        /// </summary>
        public double LastValue { get; set; }
        
        /// <summary>
        /// When this metric was last updated
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Average value over time (if tracking enabled)
        /// </summary>
        public double AverageValue { get; set; }
        
        /// <summary>
        /// Minimum value observed
        /// </summary>
        public double MinValue { get; set; } = double.MaxValue;
        
        /// <summary>
        /// Maximum value observed
        /// </summary>
        public double MaxValue { get; set; } = double.MinValue;
        
        /// <summary>
        /// Count of observations
        /// </summary>
        public long ObservationCount { get; set; }
        
        /// <summary>
        /// Updates the metric with a new value
        /// </summary>
        public void Update(double value)
        {
            LastValue = value;
            Timestamp = DateTime.UtcNow;
            
            // Update statistics
            ObservationCount++;
            MinValue = Math.Min(MinValue, value);
            MaxValue = Math.Max(MaxValue, value);
            
            // Compute running average
            if (ObservationCount == 1)
            {
                AverageValue = value;
            }
            else
            {
                // Weighted average with more weight to recent values (0.9 for new, 0.1 for history)
                AverageValue = (AverageValue * 0.1) + (value * 0.9);
            }
        }
    }
}