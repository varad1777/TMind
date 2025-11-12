using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace MyApp.Infrastructure.SignalRHub
{
    public class ModbusHub : Hub
    {
        public Task SubscribeToDevice(string deviceId) =>
            Groups.AddToGroupAsync(Context.ConnectionId, deviceId);

        public Task UnsubscribeFromDevice(string deviceId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, deviceId);

        public Task Ping() => Task.CompletedTask;
    }
}
