using System;
using System.Collections.Generic;
using System.Linq;

namespace XPlain.Services
{
    public static class ListExtensions
    {
        public static double StdDev(this IEnumerable<double> values)
        {
            var mean = values.Average();
            var sumOfSquaresOfDifferences = values.Select(val => Math.Pow(val - mean, 2)).Sum();
            var variance = sumOfSquaresOfDifferences / (values.Count() - 1);
            return Math.Sqrt(variance);
        }
    }
}