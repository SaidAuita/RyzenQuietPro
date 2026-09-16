using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuietPro
{
    public enum FanPluginStatus
    {
        Disabled,
        NotInstalled,
        Starting,
        Connecting,
        Connected,
        Disconnected,
        Error
    }

    public class FanItem
    {
        public string Id { get; }
        public string Name { get; set; }
        public string Hardware { get; }
        public string HardwareType { get; }
        public int CurrentRpm { get; set; }
        public int MaxRpm { get; set; } = 2000;
        public Color Color { get; set; }

        private readonly List<float> _history = new();
        private readonly object _lock = new();

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock) return _history.ToArray();
            }
        }

        public FanItem(string id, string name, string hardware, string hardwareType, Color color)
        {
            Id = id;
            Name = name;
            Hardware = hardware;
            HardwareType = hardwareType;
            Color = color;

            lock (_lock)
            {
                for (int i = 0; i < 60; i++) _history.Add(0f);
            }
        }

        public void AddSample(int rpm)
        {
            CurrentRpm = Math.Max(0, rpm);
            if (CurrentRpm > MaxRpm)
            {
                MaxRpm = Math.Max(MaxRpm, ((CurrentRpm / 500) + 1) * 500);
            }

            lock (_lock)
            {
                _history.Add(CurrentRpm);
                while (_history.Count > 60) _history.RemoveAt(0);
            }
        }
    }

    public class FanMonitorClient : IDisposable
    {
        private const string PipeName = "RyzenQuietPro_Fans";

        private static readonly Color[] Palette = new[]
        {
            Color.FromArgb(56, 189, 248),  // Sky Blue (CPU Fan)
            Color.FromArgb(34, 197, 94),   // Emerald (Chassis 1)
            Color.FromArgb(249, 115, 22),  // Orange (Chassis 2 / Pump)
            Color.FromArgb(168, 85, 247),  // Purple (GPU Fan)
            Color.FromArgb(236, 72, 153),  // Pink (Chassis 3)
            Color.FromArgb(234, 179, 8),   // Amber
            Color.FromArgb(20, 184, 166)   // Teal
        };

        private readonly List<FanItem> _fans = new();
        private readonly List<FanItem> _demoFans = new();
        private readonly object _fansLock = new();
        private bool _demoMode;
        private double _demoStep = 0;

        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private Process? _serviceProcess;
        private StreamWriter? _pipeWriter;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingCommands = new();
        private readonly System.Threading.SemaphoreSlim _writeLock = new(1, 1);

        public FanPluginStatus Status { get; private set; } = FanPluginStatus.Disabled;
        public string StatusMessage { get; private set; } = string.Empty;

        public bool DemoMode
        {
            get => _demoMode;
            set
            {
                if (_demoMode != value)
                {
                    _demoMode = value;
                    if (_demoMode)
                    {
                        InitDemoFans();
                        SetStatus(FanPluginStatus.Connected, Loc.Get("FanPluginStatus_Connected"));
                    }
                    else
                    {
                        if (Status != FanPluginStatus.Connected)
                        {
                            SetStatus(FanPluginStatus.Disabled, Loc.Get("FanPluginStatus_Disabled"));
                        }
                    }
                    FansUpdated?.Invoke();
                    StatusChanged?.Invoke();
                }
            }
        }

        public event Action? FansUpdated;
        public event Action? StatusChanged;

        public IReadOnlyList<FanItem> Fans
        {
            get
            {
                if (_demoMode)
                {
                    lock (_fansLock)
                    {
                        if (_demoFans.Count == 0) InitDemoFans();
                        return _demoFans.ToArray();
                    }
                }
                lock (_fansLock) return _fans.ToArray();
            }
        }

        public int FanCount
        {
            get
            {
                lock (_fansLock) return _demoMode ? _demoFans.Count : _fans.Count;
            }
        }

        public bool IsConnected => _demoMode || (Status == FanPluginStatus.Connected);

        private void InitDemoFans()
        {
            lock (_fansLock)
            {
                if (_demoFans.Count > 0) return;
                var cpuFan = new FanItem("demo_cpu", "CPU Fan", "Motherboard", "Fan", Palette[0]);
                cpuFan.AddSample(1150);

                var gpuFan = new FanItem("demo_gpu", "GPU Fan", "GPU", "Fan", Palette[3]);
                gpuFan.AddSample(1850);

                var ch1Fan = new FanItem("demo_ch1", "Chassis #1", "Motherboard", "Fan", Palette[1]);
                ch1Fan.AddSample(2850);

                var ch2Fan = new FanItem("demo_ch2", "Chassis #2", "Motherboard", "Fan", Palette[4]);
                ch2Fan.AddSample(950);

                var pumpFan = new FanItem("demo_pump", "Pump Fan", "Cooler", "Fan", Palette[2]);
                pumpFan.AddSample(3400);

                _demoFans.Add(cpuFan);
                _demoFans.Add(gpuFan);
                _demoFans.Add(ch1Fan);
                _demoFans.Add(ch2Fan);
                _demoFans.Add(pumpFan);
            }
        }

        public static string? LocatePluginExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(baseDir, "plugins", "FanService", "RyzenQuiet.FanService.exe"),
                Path.Combine(baseDir, "plugins", "RyzenQuiet.FanService.exe"),
                Path.Combine(baseDir, "RyzenQuiet.FanService.exe"),
                Path.Combine(baseDir, "..", "build", "plugins", "FanService", "RyzenQuiet.FanService.exe"),
                Path.Combine(baseDir, "..", "..", "..", "build", "plugins", "FanService", "RyzenQuiet.FanService.exe")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }

        public static bool IsPluginInstalled => LocatePluginExecutable() != null;

        public static void OpenPluginFolder()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string pluginsDir = Path.Combine(baseDir, "plugins", "FanService");
                if (!Directory.Exists(pluginsDir))
                {
                    Directory.CreateDirectory(pluginsDir);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{pluginsDir}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to open plugin directory: {ex.Message}");
            }
        }

        public bool IsRunning => Status == FanPluginStatus.Connected || Status == FanPluginStatus.Connecting || Status == FanPluginStatus.Starting;

        public void StartService()
        {
            SetEnabled(true);
        }

        public async Task SendCommandAsync(string command)
        {
            var writer = _pipeWriter;
            if (writer != null)
            {
                await _writeLock.WaitAsync();
                try
                {
                    await writer.WriteLineAsync(command);
                    await writer.FlushAsync();
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Log($"FanMonitorClient SendCommand error: {ex.Message}");
                }
                finally
                {
                    _writeLock.Release();
                }
            }

            _pendingCommands.Enqueue(command);
        }

        public void TriggerGpuFanTest()
        {
            _ = SendCommandAsync("TEST_GPU_FAN");
        }

        public void SendGpuPowerLimit(int watts)
        {
            _ = SendCommandAsync($"SET_GPU_POWER:{watts}");
        }

        public void ResetGpuPowerLimit(int stockWatts = 336)
        {
            _ = SendCommandAsync($"RESET_GPU_POWER:{stockWatts}");
        }

        public void SendGpuFanCap(int percent)
        {
            _ = SendCommandAsync($"SET_GPU_FAN_MAX:{percent}");
        }

        public void ResetGpuFan()
        {
            _ = SendCommandAsync("RESET_GPU_FAN");
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (_workerTask != null && !_workerTask.IsCompleted) return;

                string? exePath = LocatePluginExecutable();
                if (exePath == null)
                {
                    SetStatus(FanPluginStatus.NotInstalled, Loc.Get("FanPluginStatus_NotInstalled"));
                    return;
                }

                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                _workerTask = Task.Run(() => WorkerLoopAsync(exePath, _cts.Token));
            }
            else
            {
                StopService();
            }
        }

        private void SetStatus(FanPluginStatus status, string message = "")
        {
            if (Status != status || StatusMessage != message)
            {
                Status = status;
                StatusMessage = message;
                StatusChanged?.Invoke();
            }
        }

        private async Task WorkerLoopAsync(string exePath, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Ensure process is running
                    EnsureProcessRunning(exePath);

                    SetStatus(FanPluginStatus.Connecting, Loc.Get("FanPluginStatus_Connecting"));

                    using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                    // Try connecting to pipe
                    try
                    {
                        await pipeClient.ConnectAsync(3000, ct);
                    }
                    catch (TimeoutException)
                    {
                        // Retry loop
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    if (ct.IsCancellationRequested) break;

                    SetStatus(FanPluginStatus.Connected, Loc.Get("FanPluginStatus_Connected"));

                    using var reader = new StreamReader(pipeClient, Encoding.UTF8, false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(pipeClient, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
                    _pipeWriter = writer;

                    // Drain any commands queued before connection was established
                    await _writeLock.WaitAsync(ct);
                    try
                    {
                        while (_pendingCommands.TryDequeue(out var pendingCmd))
                        {
                            await writer.WriteLineAsync(pendingCmd);
                            await writer.FlushAsync();
                            Logger.Log($"[FanMonitorClient] Sent queued command to FanService: {pendingCmd}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[FanMonitorClient] Error sending queued command: {ex.Message}");
                    }
                    finally
                    {
                        _writeLock.Release();
                    }

                    try
                    {
                        while (!ct.IsCancellationRequested && pipeClient.IsConnected)
                        {
                            string? line = await reader.ReadLineAsync(ct);
                            if (line == null) break;

                            ProcessSnapshotJson(line);
                        }
                    }
                    finally
                    {
                        _pipeWriter = null;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"FanMonitorClient loop warning: {ex.Message}");
                    SetStatus(FanPluginStatus.Disconnected, Loc.Get("FanPluginStatus_Connecting"));
                    try { await Task.Delay(2000, ct); } catch { }
                }
            }
        }

        private void EnsureProcessRunning(string exePath)
        {
            try
            {
                // Check if already running
                var existing = Process.GetProcessesByName("RyzenQuiet.FanService");
                if (existing.Length > 0)
                {
                    _serviceProcess = existing[0];
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                _serviceProcess = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception w32Ex) when (w32Ex.NativeErrorCode == 1223) // ERROR_CANCELLED (UAC refused)
            {
                SetStatus(FanPluginStatus.Error, Loc.Get("FanPluginStatus_NeedAdmin"));
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to launch FanService process: {ex.Message}");
                SetStatus(FanPluginStatus.Error, ex.Message);
            }
        }

        private class FanMetricDto
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Hardware { get; set; }
            public string? HardwareType { get; set; }
            public int Rpm { get; set; }
        }

        private class FanSnapshotDto
        {
            public string? Status { get; set; }
            public DateTime Timestamp { get; set; }
            public List<FanMetricDto>? Fans { get; set; }
        }

        private void ProcessSnapshotJson(string json)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<FanSnapshotDto>(json);
                if (snapshot?.Fans == null) return;

                lock (_fansLock)
                {
                    // Update or insert fan items
                    foreach (var dto in snapshot.Fans)
                    {
                        if (string.IsNullOrEmpty(dto.Id)) continue;

                        var existing = _fans.Find(f => f.Id == dto.Id);
                        if (existing == null)
                        {
                            Color assignedColor = Palette[_fans.Count % Palette.Length];
                            string name = dto.Name ?? "Fan";
                            existing = new FanItem(
                                dto.Id,
                                name,
                                dto.Hardware ?? "Hardware",
                                dto.HardwareType ?? "Fan",
                                assignedColor
                            );
                            _fans.Add(existing);
                        }

                        existing.AddSample(dto.Rpm);
                    }
                }

                FansUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error parsing FanSnapshot JSON: {ex.Message}");
            }
        }

        public void Sample()
        {
            if (_demoMode)
            {
                lock (_fansLock)
                {
                    _demoStep += 0.25;
                    if (_demoFans.Count >= 5)
                    {
                        _demoFans[0].AddSample((int)(1150 + 60 * Math.Sin(_demoStep)));
                        _demoFans[1].AddSample((int)(1850 + 120 * Math.Cos(_demoStep * 0.8)));
                        _demoFans[2].AddSample((int)(2850 + 150 * Math.Sin(_demoStep * 1.2)));
                        _demoFans[3].AddSample(0); // Fan #2 stays at 0 to show stopped state
                        _demoFans[4].AddSample((int)(3400 + 80 * Math.Sin(_demoStep * 0.5)));
                        FansUpdated?.Invoke();
                    }
                }
            }
        }

        public void StopService()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                client.Connect(300);
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine("QUIT");
            }
            catch { }

            try
            {
                var procs = Process.GetProcessesByName("RyzenQuiet.FanService");
                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.WaitForExit(500))
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch { }

            _serviceProcess = null;
            _cts?.Dispose();
            _cts = null;
            _workerTask = null;

            lock (_fansLock)
            {
                _fans.Clear();
            }

            SetStatus(FanPluginStatus.Disabled, Loc.Get("FanPluginStatus_Disabled"));
            FansUpdated?.Invoke();
        }

        public void Dispose()
        {
            StopService();
            lock (_fansLock)
            {
                _fans.Clear();
            }
        }
    }
}
