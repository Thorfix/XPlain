using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace XPlain.Services
{
    public class ResourceMonitor
    {
        private readonly Process _currentProcess;
        private static readonly double _cpuCount = Environment.ProcessorCount;

        public ResourceMonitor()
        {
            _currentProcess = Process.GetCurrentProcess();
        }

        public async Task<ResourceMetrics> GetResourceMetricsAsync()
        {
            var cpuUsage = await GetCpuUsageAsync();
            var memoryUsage = GetMemoryUsage();
            var ioUsage = GetIoUsage();

            return new ResourceMetrics
            {
                CpuUsagePercent = cpuUsage,
                MemoryUsageMB = memoryUsage,
                IoOperationsPerSecond = ioUsage,
                AvailableMemoryMB = GetAvailableSystemMemory(),
                SystemLoad = GetSystemLoad()
            };
        }

        private async Task<double> GetCpuUsageAsync()
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = _currentProcess.TotalProcessorTime;

            await Task.Delay(500); // Sample over 500ms

            var endTime = DateTime.UtcNow;
            var endCpuUsage = _currentProcess.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPerCore = (endTime - startTime).TotalMilliseconds * _cpuCount;

            return cpuUsedMs / totalMsPerCore * 100;
        }

        private double GetMemoryUsage()
        {
            return _currentProcess.WorkingSet64 / (1024.0 * 1024.0); // Convert to MB
        }

        private double GetIoUsage()
        {
            return (_currentProcess.ReadOperationCount + _currentProcess.WriteOperationCount) / 
                   _currentProcess.TotalProcessorTime.TotalSeconds;
        }

        private double GetAvailableSystemMemory()
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0); // Convert to MB
        }

        private double GetSystemLoad()
        {
            // A simple metric combining CPU, memory, and IO load
            return (_currentProcess.ProcessorAffinity.ToInt64() / _cpuCount) * 
                   (_currentProcess.WorkingSet64 / (double)_currentProcess.PeakWorkingSet64);
        }
    }

    public record ResourceMetrics
    {
        public double CpuUsagePercent { get; init; }
        public double MemoryUsageMB { get; init; }
        public double IoOperationsPerSecond { get; init; }
        public double AvailableMemoryMB { get; init; }
        public double SystemLoad { get; init; }

        public bool CanHandleAdditionalLoad(double requiredMemoryMB)
        {
            return CpuUsagePercent < 80 && // CPU has capacity
                   AvailableMemoryMB > requiredMemoryMB * 1.5 && // Memory has buffer
                   SystemLoad < 0.8; // Overall system has capacity
        }
    }
}