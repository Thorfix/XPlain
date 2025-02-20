using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using XPlain.Services;

namespace XPlain.Hubs
{
    public class ModelMonitoringHub : Hub
    {
        private readonly IModelPerformanceMonitor _performanceMonitor;
        private readonly MLModelValidationService _validationService;

        public ModelMonitoringHub(
            IModelPerformanceMonitor performanceMonitor,
            MLModelValidationService validationService)
        {
            _performanceMonitor = performanceMonitor;
            _validationService = validationService;
        }

        public async Task SubscribeToMetrics()
        {
            var metrics = await _performanceMonitor.GetCurrentMetrics();
            await Clients.Caller.SendAsync("ModelMetricsUpdate", metrics);
        }

        public async Task SubscribeToABTest(string testId)
        {
            var results = await _validationService.GetABTestResultsAsync(testId);
            await Clients.Caller.SendAsync("ABTestUpdate", results);
        }

        public async Task GetHistoricalMetrics(string timeRange)
        {
            // Parse time range and get historical metrics
            var (startDate, endDate) = ParseTimeRange(timeRange);
            var metrics = await _performanceMonitor.GetHistoricalMetrics(startDate, endDate);
            await Clients.Caller.SendAsync("HistoricalMetricsUpdate", metrics);
        }

        private (DateTime startDate, DateTime endDate) ParseTimeRange(string range)
        {
            var end = DateTime.UtcNow;
            var start = range switch
            {
                "1h" => end.AddHours(-1),
                "24h" => end.AddHours(-24),
                "7d" => end.AddDays(-7),
                "30d" => end.AddDays(-30),
                _ => end.AddHours(-24)
            };
            return (start, end);
        }
    }
}