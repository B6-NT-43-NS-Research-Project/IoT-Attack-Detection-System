using IDSServer.Hubs;
using IDSServer.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace IDSServer.Services
{
    public interface ITrafficAnalyzer
    {
        Task AnalyzePacketAsync(PacketLog packet, string connectionId);
    }

    public class TrafficAnalyzer : ITrafficAnalyzer
    {
        private readonly IHubContext<IdsHub> _hubContext;
        private readonly ConcurrentDictionary<string, TrafficStats> _ipTrafficStats = new();

        public TrafficAnalyzer(IHubContext<IdsHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task AnalyzePacketAsync(PacketLog packet, string connectionId)
        {
            var srcIp = packet.SourceIp;
            var dstIp = packet.DestinationIp;
            var now = DateTime.Now;

            // Track statistics for the SENDER (Source IP)
            var stats = _ipTrafficStats.GetOrAdd(srcIp, new TrafficStats { FirstPacketTime = now });
            stats.PacketCount++;
            stats.LastDestination = dstIp; // Track who they are talking to

            var duration = (now - stats.FirstPacketTime).TotalSeconds;

            // Reset monitoring window every 5 seconds
            if (duration > 5)
            {
                stats.FirstPacketTime = now;
                stats.PacketCount = 1;
                return;
            }

            // --- BIDIRECTIONAL ANALYSIS ---
            if (duration >= 1)
            {
                double packetsPerSec = stats.PacketCount / duration;

                // Rule 1: High Rate (Flood/DoS)
                if (packetsPerSec > 50 && (now - stats.LastAlertTime).TotalSeconds > 10)
                {
                    stats.LastAlertTime = now;
                    // Alert Format: "High Traffic Detected"
                    await SendAlertAsync(connectionId, "High", $"High Traffic Volume ({packetsPerSec:F0} pkts/sec)", srcIp, dstIp);
                }
                // Rule 2: Suspicious Activity
                else if (packetsPerSec > 20 && packetsPerSec <= 50 && (now - stats.LastAlertTime).TotalSeconds > 10)
                {
                    stats.LastAlertTime = now;
                    await SendAlertAsync(connectionId, "Medium", "Abnormal Traffic Pattern", srcIp, dstIp);
                }
            }
        }

        private async Task SendAlertAsync(string connectionId, string level, string message, string src, string dst)
        {
            // We now send 4 arguments: Level, Message, Source, Destination
            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveAlert", level, message, src, dst);
        }
    }

    public class TrafficStats
    {
        public int PacketCount { get; set; } = 0;
        public DateTime FirstPacketTime { get; set; }
        public DateTime LastAlertTime { get; set; } = DateTime.MinValue;
        public string LastDestination { get; set; } = "";
    }
}