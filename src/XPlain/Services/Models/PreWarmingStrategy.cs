using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public class PreWarmingStrategy
    {
        public Dictionary<string, PreWarmPriority> KeyPriorities { get; set; } = new Dictionary<string, PreWarmPriority>();
        public int BatchSize { get; set; } = 10;
        public TimeSpan PreWarmInterval { get; set; } = TimeSpan.FromMinutes(15);
        public double ResourceThreshold { get; set; } = 0.8;
        public Dictionary<string, DateTime> OptimalTimings { get; set; } = new Dictionary<string, DateTime>();
    }
}