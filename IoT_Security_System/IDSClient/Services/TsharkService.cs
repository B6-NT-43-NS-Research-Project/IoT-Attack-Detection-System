using System;
using System.Diagnostics;

namespace IDSClient.Services
{
    public class TsharkService
    {
        // FIX: '?' makes the event nullable
        public event Action<string>? OnDataReceived;

        public void StartCapture(string interfaceName)
        {
            var info = new ProcessStartInfo
            {
                FileName = "tshark",
                Arguments = $"-i {interfaceName} -l -T fields -e ip.src -e ip.dst -e _ws.col.Protocol",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = info };
            process.OutputDataReceived += (s, e) =>
            {
                // FIX: Check for valid data before sending
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OnDataReceived?.Invoke(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
        }
    }
}