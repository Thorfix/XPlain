using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting.Scenarios
{
    public class MitigationTestScenario : ILoadTestScenario
    {
        private readonly AutomaticMitigationService _mitigationService;
        private readonly ICacheProvider _cacheProvider;
        private readonly QueryDistributionGenerator _queryGenerator;

        public string Name => "Mitigation Strategy Test";
        public LoadTestProfile Profile { get; }

        public MitigationTestScenario(
            AutomaticMitigationService mitigationService,
            ICacheProvider cacheProvider,
            QueryDistributionGenerator queryGenerator,
            LoadTestProfile profile)
        {
            _mitigationService = mitigationService;
            _cacheProvider = cacheProvider;
            _queryGenerator = queryGenerator;
            Profile = profile;
        }

        public async Task ExecuteAsync(ILoadTestContext context, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var query = _queryGenerator.GenerateQuery();
                
                await context.SimulateUserActionAsync(async () =>
                {
                    // Track pre-mitigation state
                    var initialState = await _mitigationService.GetCurrentStateAsync();
                    
                    // Perform cache operation
                    var result = await _cacheProvider.TryGetAsync(query, cancellationToken);
                    
                    // Track post-mitigation state
                    var finalState = await _mitigationService.GetCurrentStateAsync();
                    
                    // Log mitigation effectiveness
                    await context.LogMetricAsync("mitigation_response_time", 
                        finalState.ResponseTime - initialState.ResponseTime);
                    await context.LogMetricAsync("mitigation_active",
                        finalState.MitigationActive ? 1.0 : 0.0);
                    await context.LogMetricAsync("mitigation_strategy",
                        (double)finalState.CurrentStrategy);
                });

                await Task.Delay(_queryGenerator.GetNextQueryDelay(), cancellationToken);
            }
        }
    }
}