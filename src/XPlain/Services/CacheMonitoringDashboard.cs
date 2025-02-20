using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class CacheMonitoringDashboard
    {
        private readonly ILogger<CacheMonitoringDashboard> _logger;
        private readonly ICacheMonitoringService _cacheMonitoring;
        private readonly IModelPerformanceMonitor _modelMonitor;
        private readonly CacheMonitoringHub _monitoringHub;

        public CacheMonitoringDashboard(
            ILogger<CacheMonitoringDashboard> logger,
            ICacheMonitoringService cacheMonitoring,
            IModelPerformanceMonitor modelMonitor,
            CacheMonitoringHub monitoringHub)
        {
            _logger = logger;
            _cacheMonitoring = cacheMonitoring;
            _modelMonitor = modelMonitor;
            _monitoringHub = monitoringHub;
        }

        public async Task UpdateDashboard()
        {
            try
            {
                // Get cache performance metrics
                var cacheMetrics = await _cacheMonitoring.GetPerformanceMetricsAsync();
                
                // Get model performance metrics
                var modelMetrics = await _modelMonitor.GetCurrentMetrics();
                
                // Combine metrics for dashboard
                var dashboardData = new
                {
                    CachePerformance = cacheMetrics,
                    ModelPerformance = modelMetrics,
                    LastUpdate = DateTime.UtcNow
                };

                // Send update through SignalR hub
                await _monitoringHub.SendAsync("UpdateDashboard", JsonSerializer.Serialize(dashboardData));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating monitoring dashboard");
            }
        }

        public async Task<DashboardMetrics> GetDashboardMetrics()
        {
            var cacheMetrics = await _cacheMonitoring.GetPerformanceMetricsAsync();
            var modelMetrics = await _modelMonitor.GetCurrentMetrics();
            var alerts = await _cacheMonitoring.GetActiveAlertsAsync();

            return new DashboardMetrics
            {
                CachePerformance = cacheMetrics,
                ModelPerformance = modelMetrics,
                ActiveAlerts = alerts,
                LastUpdate = DateTime.UtcNow
            };
        }

        public async Task<List<PerformanceMetrics>> GetHistoricalPerformance(DateTime startDate, DateTime endDate)
        {
            return await _modelMonitor.GetHistoricalMetrics(startDate, endDate);
        }
    }

    public class DashboardMetrics
    {
        public object CachePerformance { get; set; }
        public PerformanceMetrics ModelPerformance { get; set; }
        public List<object> ActiveAlerts { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}