using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class LoadTestSettings
    {
        [Required]
        public int DefaultConcurrentUsers { get; set; } = 10;

        [Required]
        public int DefaultDurationSeconds { get; set; } = 300;

        [Required]
        public int DefaultRampUpSeconds { get; set; } = 30;

        public string[] TestQueries { get; set; } = new[]
        {
            "What is machine learning?",
            "Explain quantum computing",
            "How does natural language processing work?",
            "Describe artificial intelligence",
            "What are neural networks?"
        };

        public Dictionary<string, LoadTestProfile> Profiles { get; set; } = new()
        {
            ["LowLoad"] = new LoadTestProfile
            {
                ConcurrentUsers = 5,
                DurationSeconds = 300,
                RampUpSeconds = 30
            },
            ["MediumLoad"] = new LoadTestProfile
            {
                ConcurrentUsers = 25,
                DurationSeconds = 600,
                RampUpSeconds = 60
            },
            ["HighLoad"] = new LoadTestProfile
            {
                ConcurrentUsers = 50,
                DurationSeconds = 900,
                RampUpSeconds = 90
            }
        };
    }

    public class LoadTestProfile
    {
        public int ConcurrentUsers { get; set; }
        public int DurationSeconds { get; set; }
        public int RampUpSeconds { get; set; }
    }
}