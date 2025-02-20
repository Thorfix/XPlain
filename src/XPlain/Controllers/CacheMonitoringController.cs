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

        public CacheMonitoringController(ICacheMonitoringService monitoringService, ICacheProvider cacheProvider)
        {
            _monitoringService = monitoringService;
            _cacheProvider = cacheProvider;
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
    }
}