using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using XPlain.Services.LoadTesting;
using XPlain.Services.LoadTesting.Scenarios;

namespace XPlain.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoadTestController : ControllerBase
    {
        private readonly LoadTestEngine _loadTestEngine;
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _mlPredictionService;
        private static CancellationTokenSource _currentTest;

        public LoadTestController(
            LoadTestEngine loadTestEngine,
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService)
        {
            _loadTestEngine = loadTestEngine;
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartLoadTest([FromBody] LoadTestRequest request)
        {
            if (_currentTest != null && !_currentTest.Token.IsCancellationRequested)
            {
                return BadRequest("A load test is already running");
            }

            var profile = new LoadTestProfile
            {
                ConcurrentUsers = request.ConcurrentUsers,
                Duration = TimeSpan.FromSeconds(request.DurationSeconds),
                RampUpPeriod = TimeSpan.FromSeconds(request.RampUpSeconds)
            };

            ILoadTestScenario scenario = request.ScenarioType switch
            {
                "CachePerformance" => new CachePerformanceScenario(_cacheProvider, _mlPredictionService, profile),
                _ => throw new ArgumentException($"Unknown scenario type: {request.ScenarioType}")
            };

            _currentTest = new CancellationTokenSource();
            
            // Start the test in the background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _loadTestEngine.RunScenarioAsync(scenario, _currentTest.Token);
                }
                catch (OperationCanceledException)
                {
                    // Test was cancelled, this is expected
                }
            });

            return Ok(new { message = "Load test started" });
        }

        [HttpPost("stop")]
        public IActionResult StopLoadTest()
        {
            if (_currentTest == null || _currentTest.Token.IsCancellationRequested)
            {
                return BadRequest("No load test is currently running");
            }

            _currentTest.Cancel();
            return Ok(new { message = "Load test stopped" });
        }

        [HttpGet("metrics")]
        public async Task<ActionResult<LoadTestMetrics>> GetMetrics()
        {
            var metrics = await _loadTestEngine.GetCurrentMetricsAsync();
            return Ok(metrics);
        }

        [HttpGet("behavior-report")]
        public async Task<ActionResult<BehaviorSummaryReport>> GetBehaviorReport()
        {
            var report = await _loadTestEngine.GetBehaviorReport();
            return Ok(report);
        }
    }

    public class LoadTestRequest
    {
        public string ScenarioType { get; set; }
        public int ConcurrentUsers { get; set; }
        public int DurationSeconds { get; set; }
        public int RampUpSeconds { get; set; }
    }
}