using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Concurrent;
using Avalonia.Threading;
using IDSClient.Models;
using System.Collections.Generic;

namespace IDSClient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private HubConnection? _hubConnection;
        private Process? _tsharkProcess;

        // --- PERFORMANCE & CONTROL ---
        private ConcurrentQueue<string> _logQueue = new();
        private System.Timers.Timer? _uiRefreshTimer;

        // NEW: A Hard Switch to stop processing immediately
        private volatile bool _isCapturing = false;
        // -----------------------------

        private ConcurrentDictionary<string, string> _dnsCache = new();
        private int _packetCountInternal = 0;
        private System.Timers.Timer? _rateTimer;

        [ObservableProperty] private int _currentTabIndex = 0;

        // --- CONFIGURATION ---
        [ObservableProperty] private string _serverIp = "vps.sierra.ddns-ip.net";
        [ObservableProperty] private string _serverPort = "5000";
        [ObservableProperty] private string _secretKey = "";
        [ObservableProperty] private string _interfaceName = "";

        [ObservableProperty] private string _statusMessage = "Ready to Connect";
        [ObservableProperty] private string _statusColor = "Gray";
        [ObservableProperty] private bool _isConnected = false;

        // --- LIVE DATA ---
        [ObservableProperty] private string _packetsPerSecond = "0";
        [ObservableProperty] private int _threatCount = 0;
        [ObservableProperty] private string _threatStatus = "SYSTEM SECURED";
        [ObservableProperty] private string _threatColor = "#00FF00";

        public ObservableCollection<string> AvailableInterfaces { get; } = new();
        public ObservableCollection<string> LiveLogs { get; } = new();
        public ObservableCollection<AlertItem> HistoryLogs { get; } = new();

        public MainViewModel()
        {
            LoadInterfaces();

            // UI Batch Timer (Always runs, but only does work if Queue has items)
            _uiRefreshTimer = new System.Timers.Timer(250);
            _uiRefreshTimer.Elapsed += ProcessLogQueue;
            _uiRefreshTimer.Start();
        }

        private void ProcessLogQueue(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // STOP CHECK: If we aren't capturing, don't update the UI
            if (!_isCapturing || _logQueue.IsEmpty) return;

            var newLogs = new List<string>();
            while (_logQueue.TryDequeue(out string? log) && newLogs.Count < 50)
            {
                if (log != null) newLogs.Add(log);
            }

            if (newLogs.Count > 0)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Double check in case user clicked stop while we were invoking
                    if (!_isCapturing) return;

                    foreach (var log in newLogs)
                    {
                        LiveLogs.Insert(0, log);
                    }
                    while (LiveLogs.Count > 100) LiveLogs.RemoveAt(LiveLogs.Count - 1);
                });
            }
        }

        private void LoadInterfaces()
        {
            AvailableInterfaces.Clear();
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in nics) AvailableInterfaces.Add(nic.Name);
                InterfaceName = AvailableInterfaces.FirstOrDefault(x => x.ToLower().Contains("wi-fi") || x.ToLower().Contains("wlan"))
                                ?? AvailableInterfaces.FirstOrDefault() ?? "wlan0";
            }
            catch { AvailableInterfaces.Add("wlan0"); InterfaceName = "wlan0"; }
        }

        [RelayCommand] public void GoToDashboard() => CurrentTabIndex = 0;
        [RelayCommand] public void GoToHistory() => CurrentTabIndex = 1;
        [RelayCommand] public void GoToLiveFeed() => CurrentTabIndex = 2;

        [RelayCommand]
        public async Task ConnectSystem()
        {
            if (string.IsNullOrWhiteSpace(ServerIp)) return;

            StatusMessage = "Connecting...";
            StatusColor = "Orange";
            string url = $"http://{ServerIp}:{ServerPort}/idshub";

            try
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url, options => options.Headers.Add("X-Secret-Key", SecretKey))
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.On<string, string, string, string>("ReceiveAlert", (level, msg, srcIp, dstIp) =>
                {
                    string srcName = GetHostName(srcIp);
                    string dstName = GetHostName(dstIp);

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        string readableMsg = $"{srcName} ({srcIp}) \u2192 {dstName} ({dstIp})";
                        string fullDetail = $"{msg}. \nSource: {srcName}\nTarget: {dstName}";

                        ThreatStatus = $"ALERT: {msg}";
                        ThreatColor = level == "High" ? "Red" : "Orange";
                        ThreatCount++;

                        HistoryLogs.Insert(0, new AlertItem
                        {
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            Level = level,
                            Message = readableMsg,
                            Detail = fullDetail,
                            Color = level == "High" ? "Red" : "Orange"
                        });
                    });
                });

                await _hubConnection.StartAsync();

                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    string deviceId = Environment.MachineName ?? "Unknown-Sentry";
                    await _hubConnection.InvokeAsync("RegisterClient", deviceId);
                }

                StatusMessage = "SECURE CONNECTION ESTABLISHED";
                StatusColor = "Green";
                IsConnected = true;
                ThreatStatus = "SYSTEM SECURED";
                ThreatColor = "#00FF00";
                ThreatCount = 0;

                // START CAPTURE
                _isCapturing = true;
                _logQueue.Clear(); // Ensure clean slate

                _rateTimer = new System.Timers.Timer(1000);
                _rateTimer.Elapsed += (s, e) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        PacketsPerSecond = _packetCountInternal.ToString();
                        _packetCountInternal = 0;
                    });
                };
                _rateTimer.Start();
                StartTshark();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed: {ex.Message}";
                StatusColor = "Red";
                IsConnected = false;
            }
        }

        [RelayCommand]
        public async Task DisconnectSystem()
        {
            // 1. HARD STOP - Stops UI updates immediately
            _isCapturing = false;

            // 2. Kill Tshark Process
            StopTshark();

            // 3. Clear the Buffer so no old data pops up
            _logQueue.Clear();

            // 4. Stop Rate Timer
            if (_rateTimer != null) { _rateTimer.Stop(); _rateTimer.Dispose(); _rateTimer = null; }
            PacketsPerSecond = "0";

            // 5. Close Connection
            if (_hubConnection != null) await _hubConnection.DisposeAsync();

            StatusMessage = "Disconnected";
            StatusColor = "Gray";
            IsConnected = false;
            ThreatStatus = "MONITORING STOPPED";
            ThreatColor = "Gray";
        }

        private string GetHostName(string ip)
        {
            if (_dnsCache.TryGetValue(ip, out string cachedName)) return cachedName;
            _dnsCache.TryAdd(ip, ip);
            Task.Run(async () =>
            {
                try
                {
                    var entry = await System.Net.Dns.GetHostEntryAsync(ip);
                    string name = entry.HostName.Split('.')[0];
                    _dnsCache[ip] = name;
                }
                catch { _dnsCache[ip] = "Unknown"; }
            });
            return ip;
        }

        private void StartTshark()
        {
            try
            {
                StopTshark();
                var info = new ProcessStartInfo
                {
                    FileName = "tshark",
                    Arguments = $"-i \"{InterfaceName}\" -l -T fields -e ip.src -e ip.dst -e _ws.col.Protocol -e frame.len",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _tsharkProcess = new Process { StartInfo = info };
                _tsharkProcess.OutputDataReceived += async (s, e) =>
                {
                    // Check Hard Stop Flag
                    if (!_isCapturing || string.IsNullOrEmpty(e.Data)) return;

                    try
                    {
                        var parts = e.Data.Split('\t');
                        if (parts.Length >= 3)
                        {
                            _packetCountInternal++;

                            string srcName = GetHostName(parts[0]);
                            string dstName = GetHostName(parts[1]);
                            string time = DateTime.Now.ToString("HH:mm:ss");

                            var packet = new PacketLog
                            {
                                ClientId = Environment.MachineName,
                                SourceIp = parts[0],
                                DestinationIp = parts[1],
                                Protocol = parts[2],
                                Length = (parts.Length > 3 && int.TryParse(parts[3], out int len)) ? len : 0,
                                Timestamp = DateTime.Now
                            };

                            string displayLog = $"[{time}] {srcName} ({packet.SourceIp}) -> {dstName} ({packet.DestinationIp}) [{packet.Protocol}]";

                            // Only enqueue if we are still capturing
                            if (_isCapturing) _logQueue.Enqueue(displayLog);

                            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                            {
                                await _hubConnection.SendAsync("SendPacketData", packet);
                            }
                        }
                    }
                    catch { }
                };
                _tsharkProcess.Start();
                _tsharkProcess.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.InvokeAsync(() => LiveLogs.Add($"[ERROR] Tshark failed: {ex.Message}"));
            }
        }

        private void StopTshark()
        {
            try { if (_tsharkProcess != null && !_tsharkProcess.HasExited) { _tsharkProcess.Kill(); _tsharkProcess.Dispose(); _tsharkProcess = null; } } catch { }
        }
    }

    public class AlertItem
    {
        public string Time { get; set; } = "";
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Color { get; set; } = "Gray";
    }
}