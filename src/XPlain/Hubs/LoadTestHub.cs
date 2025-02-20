using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using XPlain.Services.LoadTesting;

namespace XPlain.Hubs
{
    public class LoadTestHub : Hub
    {
        private readonly LoadTestEngine _loadTestEngine;

        public LoadTestHub(LoadTestEngine loadTestEngine)
        {
            _loadTestEngine = loadTestEngine;
        }

        public async Task SubscribeToMetrics()
        {
            var metrics = await _loadTestEngine.GetCurrentMetricsAsync();
            await Clients.Caller.SendAsync("UpdateMetrics", metrics);
        }
    }
}