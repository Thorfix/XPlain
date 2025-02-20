using Microsoft.AspNetCore.Mvc;
using XPlain.Services;

namespace XPlain.Controllers
{
    [ApiController]
    [Route("api/cache")]
    public class CacheMonitoringController : ControllerBase
    {
        private readonly ICacheMonitoringService _monitoringService;
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _predictionService;

        public CacheMonitoringController(
            ICacheMonitoringService monitoringService, 
            ICacheProvider cacheProvider,
            MLPredictionService predictionService)
        {
            _monitoringService = monitoringService;
            _cacheProvider = cacheProvider;
            _predictionService = predictionService;
        }

        [HttpGet("predictions")]
        public async Task<IActionResult> GetPredictions()
        {
            var predictions = await _predictionService.PredictPerformanceMetrics();
            return Ok(predictions);
        }

        [HttpGet("alerts/predicted")]
        public async Task<IActionResult> GetPredictedAlerts()
        {
            var alerts = await _predictionService.GetPredictedAlerts();
            return Ok(alerts);
        }

        [HttpGet("metrics/trends")]
        public async Task<IActionResult> GetMetricTrends()
        {
            var trends = await _predictionService.AnalyzeTrends();
            return Ok(trends);
        }

        [HttpGet("health")]
        public IActionResult GetHealth()
        {
            var health = _monitoringService.GetCacheHealth();
            return Ok(health);
        }

        [HttpGet("metrics")]
        public IActionResult GetMetrics()
        {
            var metrics = _monitoringService.GetPerformanceMetrics();
            return Ok(metrics);
        }

        [HttpGet("analytics/{days:int}")]
        public IActionResult GetAnalytics(int days)
        {
            var analytics = _monitoringService.GetHistoricalAnalytics(days);
            return Ok(analytics);
        }

        [HttpGet("alerts")]
        public IActionResult GetAlerts()
        {
            var alerts = _monitoringService.GetActiveAlerts();
            return Ok(alerts);
        }

        [HttpGet("stats")]
        public IActionResult GetStatistics()
        {
            var stats = _monitoringService.GetCacheStatistics();
            return Ok(stats);
        }

        [HttpGet("circuit-breaker")]
        public async Task<IActionResult> GetCircuitBreakerState()
        {
            var state = await _monitoringService.GetCircuitBreakerStatusAsync();
            return Ok(state);
        }

        [HttpGet("circuit-breaker/history")]
        public async Task<IActionResult> GetCircuitBreakerHistory()
        {
            var history = await _monitoringService.GetCircuitBreakerHistoryAsync();
            return Ok(history);
        }

        [HttpGet("encryption")]
        public async Task<IActionResult> GetEncryptionStatus()
        {
            var status = await _monitoringService.GetEncryptionStatusAsync();
            return Ok(status);
        }

        [HttpGet("encryption/rotation")]
        public async Task<IActionResult> GetKeyRotationSchedule()
        {
            var schedule = await _monitoringService.GetKeyRotationScheduleAsync();
            return Ok(schedule);
        }

        [HttpGet("maintenance/logs")]
        public async Task<IActionResult> GetMaintenanceLogs()
        {
            var logs = await _monitoringService.GetMaintenanceLogsAsync();
            return Ok(logs);
        }
    }
}