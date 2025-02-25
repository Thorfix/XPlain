using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public enum PreWarmPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class PreWarmingStrategy
    {
        public Dictionary<string, PreWarmPriority> KeyPriorities { get; set; } = new();
        public int BatchSize { get; set; } = 10;
        public TimeSpan PreWarmInterval { get; set; } = TimeSpan.FromMinutes(15);
        public double ResourceThreshold { get; set; } = 0.7;
        public Dictionary<string, DateTime> OptimalTimings { get; set; } = new();
    }
}