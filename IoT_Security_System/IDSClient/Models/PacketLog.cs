using System;

namespace IDSClient.Models
{
    public class PacketLog
    {
        public string ClientId { get; set; } = "";
        public string SourceIp { get; set; } = "";
        public string DestinationIp { get; set; } = "";
        public string RawData { get; set; } = "";
        public string Protocol { get; set; } = "";
        public int Length { get; set; }
        public DateTime Timestamp { get; set; }
    }
}