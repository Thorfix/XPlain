using System;

namespace XPlain.Services
{
    public class MetricValue
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double LastValue { get; set; }
        public double MinValue { get; set; } = double.MaxValue;
        public double MaxValue { get; set; } = double.MinValue;
        public long SampleCount { get; set; }
        
        public double MovingAverage { get; private set; }
        
        public void Update(double value)
        {
            LastValue = value;
            MinValue = Math.Min(MinValue, value);
            MaxValue = Math.Max(MaxValue, value);
            SampleCount++;
            
            // Calculate moving average with more weight to recent samples
            MovingAverage = SampleCount == 1 
                ? value 
                : MovingAverage * 0.7 + value * 0.3;
                
            Timestamp = DateTime.UtcNow;
        }
    }
}