using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using XPlain.Services;

namespace XPlain.Hubs
{
    public class CacheMonitoringHub : Hub
    {
        private readonly ICacheMonitoringService _monitoringService;

        public CacheMonitoringHub(ICacheMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
        }

        public async Task SubscribeToUpdates()
        {
            // Client is requesting to subscribe to updates
            await Groups.AddToGroupAsync(Context.ConnectionId, "MonitoringSubscribers");
        }

        public async Task UnsubscribeFromUpdates()
        {
            // Client is requesting to unsubscribe from updates
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "MonitoringSubscribers");
        }
    }
}