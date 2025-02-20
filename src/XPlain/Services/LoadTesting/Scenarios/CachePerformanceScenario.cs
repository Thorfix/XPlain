using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services.LoadTesting.Scenarios
{
    public class CachePerformanceScenario : ILoadTestScenario
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly MLPredictionService _mlPredictionService;
        private readonly string[] _testQueries;

        public string Name => "Cache Performance Test";

        public LoadTestProfile Profile { get; }

        public CachePerformanceScenario(
            ICacheProvider cacheProvider,
            MLPredictionService mlPredictionService,
            LoadTestProfile profile)
        {
            _cacheProvider = cacheProvider;
            _mlPredictionService = mlPredictionService;
            Profile = profile;
            
            // Sample test queries - in production, these would come from real query patterns
            _testQueries = new[]
            {
                "What is machine learning?",
                "Explain quantum computing",
                "How does natural language processing work?",
                "Describe artificial intelligence",
                "What are neural networks?"
            };
        }

        public async Task ExecuteAsync(ILoadTestContext context, CancellationToken cancellationToken)
        {
            var random = new Random();

            while (!cancellationToken.IsCancellationRequested)
            {
                var query = _testQueries[random.Next(_testQueries.Length)];
                
                await context.SimulateUserActionAsync(async () =>
                {
                    // Check cache hit prediction
                    var predictedHit = await _mlPredictionService.PredictCacheHitAsync(query);
                    
                    // Actual cache check
                    var cacheHit = await _cacheProvider.TryGetAsync(query, cancellationToken) != null;
                    
                    // Log prediction accuracy
                    await context.LogMetricAsync("prediction_accuracy", 
                        predictedHit == cacheHit ? 1.0 : 0.0);
                    
                    // Log cache hit rate
                    await context.LogMetricAsync("cache_hit_rate",
                        cacheHit ? 1.0 : 0.0);
                });

                // Random delay between requests (100ms to 2s)
                await Task.Delay(random.Next(100, 2000), cancellationToken);
            }
        }
    }
}