using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using XPlain.Services;

namespace XPlain.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CacheOptimizationController : ControllerBase
    {
        private readonly IAutomaticCacheOptimizer _optimizer;

        public CacheOptimizationController(IAutomaticCacheOptimizer optimizer)
        {
            _optimizer = optimizer;
        }

        [HttpGet("metrics")]
        public async Task<ActionResult<OptimizationMetrics>> GetOptimizationMetrics()
        {
            return await _optimizer.GetOptimizationMetricsAsync();
        }

        [HttpPost("emergency-override")]
        public async Task<ActionResult> SetEmergencyOverride([FromBody] bool enabled)
        {
            await _optimizer.SetEmergencyOverrideAsync(enabled);
            return Ok();
        }
    }
}