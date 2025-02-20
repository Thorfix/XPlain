using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting.Scenarios
{
    public class MitigationValidationScenario : ILoadTestScenario
    {
        private readonly AutomaticMitigationService _mitigationService;
        private readonly ICacheProvider _cacheProvider;
        private readonly QueryDistributionGenerator _queryGenerator;
        private readonly ConcurrentDictionary<string, MitigationEvent> _events;
        private readonly ILogger<MitigationValidationScenario> _logger;

        public string Name => "Mitigation Validation Test";
        public LoadTestProfile Profile { get; }

        public MitigationValidationScenario(
            AutomaticMitigationService mitigationService,
            ICacheProvider cacheProvider,
            QueryDistributionGenerator queryGenerator,
            ILogger<MitigationValidationScenario> logger,
            LoadTestProfile profile)
        {
            _mitigationService = mitigationService;
            _cacheProvider = cacheProvider;
            _queryGenerator = queryGenerator;
            _logger = logger;
            Profile = profile;
            _events = new ConcurrentDictionary<string, MitigationEvent>();
        }

        public async Task ExecuteAsync(ILoadTestContext context, CancellationToken cancellationToken)
        {
            // Test scenarios that should trigger different mitigation strategies
            await ValidateHighLoadMitigation(context, cancellationToken);
            await ValidateCacheExhaustionMitigation(context, cancellationToken);
            await ValidateErrorRateMitigation(context, cancellationToken);
            await ValidateLatencyMitigation(context, cancellationToken);
            
            // Generate final validation report
            await GenerateValidationReport(context);
        }

        private async Task ValidateHighLoadMitigation(ILoadTestContext context, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting high load mitigation validation");
            
            var initialState = await _mitigationService.GetCurrentStateAsync();
            var startTime = DateTime.UtcNow;
            
            // Generate high load
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while ((DateTime.UtcNow - startTime).TotalMinutes < 5 && !cancellationToken.IsCancellationRequested)
                    {
                        var query = _queryGenerator.GenerateQuery();
                        await SimulateRequest(query, "HighLoad", context, cancellationToken);
                        await Task.Delay(100, cancellationToken);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
            
            var finalState = await _mitigationService.GetCurrentStateAsync();
            await LogMitigationEvent("HighLoad", initialState, finalState, context);
        }

        private async Task ValidateCacheExhaustionMitigation(ILoadTestContext context, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting cache exhaustion mitigation validation");
            
            var initialState = await _mitigationService.GetCurrentStateAsync();
            var uniqueQueries = new List<string>();

            // Generate unique queries to exhaust cache
            for (int i = 0; i < 10000 && !cancellationToken.IsCancellationRequested; i++)
            {
                var query = $"unique_query_{Guid.NewGuid()}_{i}";
                uniqueQueries.Add(query);
                await SimulateRequest(query, "CacheExhaustion", context, cancellationToken);
            }

            var finalState = await _mitigationService.GetCurrentStateAsync();
            await LogMitigationEvent("CacheExhaustion", initialState, finalState, context);
        }

        private async Task ValidateErrorRateMitigation(ILoadTestContext context, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting error rate mitigation validation");
            
            var initialState = await _mitigationService.GetCurrentStateAsync();
            
            // Simulate high error rate scenario
            for (int i = 0; i < 100 && !cancellationToken.IsCancellationRequested; i++)
            {
                var query = $"error_inducing_query_{i}";
                await SimulateRequest(query, "ErrorRate", context, cancellationToken);
            }

            var finalState = await _mitigationService.GetCurrentStateAsync();
            await LogMitigationEvent("ErrorRate", initialState, finalState, context);
        }

        private async Task ValidateLatencyMitigation(ILoadTestContext context, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting latency mitigation validation");
            
            var initialState = await _mitigationService.GetCurrentStateAsync();
            
            // Simulate high latency scenario
            for (int i = 0; i < 100 && !cancellationToken.IsCancellationRequested; i++)
            {
                var query = $"latency_heavy_query_{i}";
                await SimulateRequest(query, "Latency", context, cancellationToken, true);
            }

            var finalState = await _mitigationService.GetCurrentStateAsync();
            await LogMitigationEvent("Latency", initialState, finalState, context);
        }

        private async Task SimulateRequest(string query, string scenario, ILoadTestContext context, 
            CancellationToken cancellationToken, bool injectLatency = false)
        {
            await context.SimulateUserActionAsync(async () =>
            {
                try
                {
                    if (injectLatency)
                    {
                        await Task.Delay(500, cancellationToken); // Simulate slow operation
                    }

                    var result = await _cacheProvider.TryGetAsync(query, cancellationToken);
                    
                    // Track mitigation status
                    var mitigationState = await _mitigationService.GetCurrentStateAsync();
                    await context.LogMetricAsync($"{scenario}_mitigation_active", 
                        mitigationState.MitigationActive ? 1.0 : 0.0);
                    await context.LogMetricAsync($"{scenario}_strategy_level",
                        (double)mitigationState.CurrentStrategy);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in {scenario} scenario");
                    throw;
                }
            });
        }

        private async Task LogMitigationEvent(string scenario, 
            MitigationState before, 
            MitigationState after,
            ILoadTestContext context)
        {
            var mitigationEvent = new MitigationEvent
            {
                Scenario = scenario,
                InitialState = before,
                FinalState = after,
                ResponseTime = after.ResponseTime - before.ResponseTime,
                StrategyChange = after.CurrentStrategy != before.CurrentStrategy,
                Timestamp = DateTime.UtcNow
            };

            _events.TryAdd($"{scenario}_{DateTime.UtcNow.Ticks}", mitigationEvent);

            // Log metrics
            await context.LogMetricAsync($"{scenario}_response_time_change", mitigationEvent.ResponseTime);
            await context.LogMetricAsync($"{scenario}_strategy_changed", mitigationEvent.StrategyChange ? 1.0 : 0.0);
        }

        private async Task GenerateValidationReport(ILoadTestContext context)
        {
            var report = new MitigationValidationReport
            {
                TotalEvents = _events.Count,
                ScenarioResults = _events.GroupBy(e => e.Value.Scenario)
                    .ToDictionary(
                        g => g.Key,
                        g => new ScenarioResult
                        {
                            EventCount = g.Count(),
                            StrategyChanges = g.Count(e => e.Value.StrategyChange),
                            AverageResponseTimeChange = g.Average(e => e.Value.ResponseTime),
                            MitigationEffectiveness = CalculateMitigationEffectiveness(g.Select(e => e.Value))
                        })
            };

            // Log summary metrics
            foreach (var result in report.ScenarioResults)
            {
                await context.LogMetricAsync($"{result.Key}_effectiveness", result.Value.MitigationEffectiveness);
            }

            _logger.LogInformation("Mitigation validation completed: {Report}", 
                System.Text.Json.JsonSerializer.Serialize(report));
        }

        private double CalculateMitigationEffectiveness(IEnumerable<MitigationEvent> events)
        {
            var list = events.ToList();
            if (!list.Any()) return 0;

            return list.Count(e => 
                e.FinalState.ResponseTime < e.InitialState.ResponseTime &&
                e.FinalState.ErrorRate < e.InitialState.ErrorRate) / (double)list.Count;
        }
    }

    public class MitigationEvent
    {
        public string Scenario { get; set; }
        public MitigationState InitialState { get; set; }
        public MitigationState FinalState { get; set; }
        public double ResponseTime { get; set; }
        public bool StrategyChange { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MitigationValidationReport
    {
        public int TotalEvents { get; set; }
        public Dictionary<string, ScenarioResult> ScenarioResults { get; set; }
    }

    public class ScenarioResult
    {
        public int EventCount { get; set; }
        public int StrategyChanges { get; set; }
        public double AverageResponseTimeChange { get; set; }
        public double MitigationEffectiveness { get; set; }
    }
}