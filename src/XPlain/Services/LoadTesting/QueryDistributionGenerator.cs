using System;
using System.Collections.Generic;
using System.Linq;

namespace XPlain.Services.LoadTesting
{
    public class QueryDistributionGenerator
    {
        private readonly Random _random = new();
        private readonly Dictionary<TimeSpan, QueryDistributionProfile> _timeBasedProfiles;
        private readonly Dictionary<string, double> _queryWeights;
        private readonly string[] _baseQueries;

        public QueryDistributionGenerator(IDictionary<string, double> queryWeights, QueryDistributionSettings settings)
        {
            _queryWeights = new Dictionary<string, double>(queryWeights);
            _baseQueries = queryWeights.Keys.ToArray();
            _timeBasedProfiles = new Dictionary<TimeSpan, QueryDistributionProfile>
            {
                // Business hours profile (9 AM - 5 PM)
                [TimeSpan.FromHours(9)] = new QueryDistributionProfile
                {
                    QueryFrequencyMultiplier = 1.5,
                    BurstProbability = 0.2,
                    MaxBurstSize = 50
                },
                // Peak traffic profile (12 PM - 2 PM)
                [TimeSpan.FromHours(12)] = new QueryDistributionProfile
                {
                    QueryFrequencyMultiplier = 2.0,
                    BurstProbability = 0.3,
                    MaxBurstSize = 100
                },
                // Low traffic profile (2 AM - 6 AM)
                [TimeSpan.FromHours(2)] = new QueryDistributionProfile
                {
                    QueryFrequencyMultiplier = 0.5,
                    BurstProbability = 0.1,
                    MaxBurstSize = 20
                }
            };
        }

        public string GenerateQuery()
        {
            var currentProfile = GetCurrentProfile();
            var multiplier = currentProfile.QueryFrequencyMultiplier;

            // Check for burst probability
            if (_random.NextDouble() < currentProfile.BurstProbability)
            {
                return GenerateBurstQueries(currentProfile.MaxBurstSize);
            }

            // Generate weighted random query
            var totalWeight = _queryWeights.Values.Sum() * multiplier;
            var randomValue = _random.NextDouble() * totalWeight;
            var accumulatedWeight = 0.0;

            foreach (var query in _queryWeights)
            {
                accumulatedWeight += query.Value * multiplier;
                if (randomValue <= accumulatedWeight)
                {
                    return GenerateVariation(query.Key);
                }
            }

            return _baseQueries[0];
        }

        public TimeSpan GetNextQueryDelay()
        {
            var profile = GetCurrentProfile();
            var baseDelay = TimeSpan.FromMilliseconds(100);
            return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds / profile.QueryFrequencyMultiplier);
        }

        private string GenerateBurstQueries(int maxBurst)
        {
            var burstSize = _random.Next(1, maxBurst);
            var queries = new List<string>();
            
            for (int i = 0; i < burstSize; i++)
            {
                var baseQuery = _baseQueries[_random.Next(_baseQueries.Length)];
                queries.Add(GenerateVariation(baseQuery));
            }

            return string.Join(" ", queries);
        }

        private string GenerateVariation(string baseQuery)
        {
            // Add variations to base queries to simulate real-world patterns
            var variations = new[]
            {
                baseQuery,
                $"explain {baseQuery}",
                $"how to {baseQuery}",
                $"what is {baseQuery}",
                $"{baseQuery} example",
                $"{baseQuery} tutorial"
            };

            return variations[_random.Next(variations.Length)];
        }

        private QueryDistributionProfile GetCurrentProfile()
        {
            var currentHour = DateTime.Now.TimeOfDay;
            return _timeBasedProfiles
                .OrderBy(p => p.Key)
                .FirstOrDefault(p => p.Key <= currentHour).Value
                ?? new QueryDistributionProfile
                {
                    QueryFrequencyMultiplier = 1.0,
                    BurstProbability = 0.1,
                    MaxBurstSize = 30
                };
        }
    }

    public class QueryDistributionProfile
    {
        public double QueryFrequencyMultiplier { get; set; }
        public double BurstProbability { get; set; }
        public int MaxBurstSize { get; set; }
    }

    public class QueryDistributionSettings
    {
        public Dictionary<TimeSpan, QueryDistributionProfile> TimeBasedProfiles { get; set; }
            = new Dictionary<TimeSpan, QueryDistributionProfile>();
        
        public Dictionary<string, double> DefaultQueryWeights { get; set; }
            = new Dictionary<string, double>();
    }
}