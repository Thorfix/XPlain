using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IModelPerformanceMonitor
    {
        Task TrackResponseAsync(string provider, string model, double responseTime, bool success, int tokenCount);
        Task<Dictionary<string, double>> GetModelMetricsAsync(string provider, string model);
        Task<Dictionary<string, List<TimeSeriesDataPoint>>> GetHistoricalMetricsAsync(string provider, string model, TimeSpan period);
        Task<List<ModelAlert>> GetActiveAlertsAsync();
    }

    public class TimeSeriesDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }

    public class ModelAlert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Provider { get; set; }
        public string Model { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}