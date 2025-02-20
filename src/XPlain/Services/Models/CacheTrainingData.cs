using System;
using Microsoft.ML.Data;

namespace XPlain.Services.Models
{
    public class CacheTrainingData
    {
        [LoadColumn(0)]
        public string Query { get; set; } = "";

        [LoadColumn(1)]
        public int Frequency { get; set; }

        [LoadColumn(2)]
        public int TimeOfDay { get; set; }

        [LoadColumn(3)]
        public int DayOfWeek { get; set; }

        [LoadColumn(4)]
        public double ResponseTime { get; set; }

        [LoadColumn(5)]
        public double CacheHitRate { get; set; }

        [LoadColumn(6)]
        public double ResourceUsage { get; set; }
    }
}