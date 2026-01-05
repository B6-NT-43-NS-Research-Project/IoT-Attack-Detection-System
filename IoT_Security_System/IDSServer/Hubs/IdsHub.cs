using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using IDSServer.Models;
using IDSServer.Services;

namespace IDSServer.Hubs
{
    public class IdsHub : Hub
    {
        private readonly ITrafficAnalyzer _analyzer;
        private static ConcurrentDictionary<string, string> ActiveConnections = new();

        public IdsHub(ITrafficAnalyzer analyzer)
        {
            _analyzer = analyzer;
        }

        public async Task RegisterClient(string clientId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, clientId);
            ActiveConnections.AddOrUpdate(clientId, Context.ConnectionId, (k, v) => Context.ConnectionId);
            // Test Alert
            await Clients.Caller.SendAsync("ReceiveAlert", "Low", "System Armed", "Server", "Client");
        }

        public async Task SendPacketData(PacketLog packet)
        {
            if (packet == null) return;
            if (ActiveConnections.TryGetValue(packet.ClientId, out string connectionId))
            {
                await _analyzer.AnalyzePacketAsync(packet, connectionId);
            }
        }

        // Manual Alert (Used by Python AI later) - Updated signature
        public async Task SendThreatAlert(string targetClientId, string level, string message, string src, string dst)
        {
            if (ActiveConnections.TryGetValue(targetClientId, out string connectionId))
            {
                await Clients.Client(connectionId).SendAsync("ReceiveAlert", level, message, src, dst);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var item = ActiveConnections.FirstOrDefault(kvp => kvp.Value == Context.ConnectionId);
            if (!string.IsNullOrEmpty(item.Key)) ActiveConnections.TryRemove(item.Key, out _);
            await base.OnDisconnectedAsync(exception);
        }
    }
}