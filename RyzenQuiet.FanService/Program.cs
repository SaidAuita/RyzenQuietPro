using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace RyzenQuiet.FanService
{
    public class FanMetricItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Hardware { get; set; } = string.Empty;
        public string HardwareType { get; set; } = string.Empty;
        public int Rpm { get; set; }
    }

    public class FanSnapshot
    {
        public string Status { get; set; } = "ok";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<FanMetricItem> Fans { get; set; } = new();
    }

    internal class Program
    {
        private const string PipeName = "RyzenQuietPro_Fans";
        private const string MutexName = @"Local\RyzenQuietPro_FanService_Mutex";
        private const int InactivityTimeoutSeconds = 25;

        private static Computer? _computer;
        private static readonly CancellationTokenSource _cts = new();
        private static readonly List<IControl> _appliedGpuControls = new();
        private static readonly object _gpuControlLock = new();

        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RyzenQuietPro",
            "fanservice_debug.log"
        );

        private static void Log(string msg)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        static async Task Main(string[] args)
        {
            Log("=== RyzenQuiet.FanService starting ===");
            Log($"OS: {Environment.OSVersion}, 64bit: {Environment.Is64BitProcess}, User: {Environment.UserName}");

            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                Log("Another instance is already running. Exiting.");
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                Log("ProcessExit event received.");
                Cleanup();
            };

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _cts.Cancel();
            };

            try
            {
                Log("Initializing LibreHardwareMonitor Computer...");
                _computer = new Computer
                {
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsStorageEnabled = false,
                    IsMemoryEnabled = false,
                    IsNetworkEnabled = false,
                    IsBatteryEnabled = false
                };

                _computer.Open();
                Log("Computer.Open() succeeded. Enumerating all detected hardware...");
                try
                {
                    var pawnType = typeof(Computer).Assembly.GetType("LibreHardwareMonitor.PawnIo.PawnIo");
                    bool isPawnInstalled = false;
                    if (pawnType != null)
                    {
                        var prop = pawnType.GetProperty("IsInstalled", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        isPawnInstalled = prop?.GetValue(null) is true;
                    }
                    Log($"PawnIO driver installed: {isPawnInstalled}");
                    if (!isPawnInstalled)
                    {
                        Log("NOTE: PawnIO driver is not installed. Motherboard/CPU fans cannot be detected on Windows 11 without PawnIO (https://pawnio.eu/).");
                    }
                }
                catch { }

                int totalSensors = 0;
                int fanSensors = 0;
                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();
                    Log($"[HW] Name: '{hw.Name}', Type: {hw.HardwareType}, Identifier: {hw.Identifier}");
                    foreach (var s in hw.Sensors)
                    {
                        totalSensors++;
                        if (s.SensorType == SensorType.Fan || s.SensorType == SensorType.Control)
                        {
                            fanSensors++;
                            Log($"  -> [FAN/CTRL SENSOR] '{s.Name}', Type: {s.SensorType}, Value: {s.Value}");
                        }
                        else
                        {
                            Log($"  -> [SENSOR] '{s.Name}', Type: {s.SensorType}, Value: {s.Value}");
                        }
                    }

                    foreach (var sub in hw.SubHardware)
                    {
                        sub.Update();
                        Log($"  [SubHW] Name: '{sub.Name}', Type: {sub.HardwareType}, Identifier: {sub.Identifier}");
                        foreach (var ss in sub.Sensors)
                        {
                            totalSensors++;
                            if (ss.SensorType == SensorType.Fan || ss.SensorType == SensorType.Control)
                            {
                                fanSensors++;
                                Log($"    -> [FAN/CTRL SENSOR] '{ss.Name}', Type: {ss.SensorType}, Value: {ss.Value}");
                            }
                            else
                            {
                                Log($"    -> [SENSOR] '{ss.Name}', Type: {ss.SensorType}, Value: {ss.Value}");
                            }
                        }
                    }
                }

                Log($"Enumeration complete. Total sensors: {totalSensors}, Fan/Control sensors: {fanSensors}");
            }
            catch (Exception ex)
            {
                Log($"Computer init EXCEPTION: {ex}");
                return;
            }

            try
            {
                await RunServerAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Log($"RunServerAsync EXCEPTION: {ex}");
            }
            finally
            {
                Log("FanService cleaning up and exiting.");
                Cleanup();
            }
        }

        private static NamedPipeServerStream CreateSecurePipeServer()
        {
            var pipeSecurity = new PipeSecurity();

            // 1. Allow Everyone (WorldSid) ReadWrite
            var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(worldSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            // 2. Allow Authenticated Users ReadWrite
            var authSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(authSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            // 3. Allow Current User / Admin FullControl
            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid != null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(currentSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            }

            return NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 4096,
                outBufferSize: 4096,
                pipeSecurity: pipeSecurity
            );
        }

        private static async Task RunServerAsync(CancellationToken ct)
        {
            DateTime lastClientActive = DateTime.UtcNow;
            Log("NamedPipe server listening started...");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipeServer = CreateSecurePipeServer();

                    // Wait for connection with inactivity check
                    var connectTask = pipeServer.WaitForConnectionAsync(ct);
                    while (!connectTask.IsCompleted)
                    {
                        var completed = await Task.WhenAny(connectTask, Task.Delay(1000, ct));
                        if (completed != connectTask)
                        {
                            if ((DateTime.UtcNow - lastClientActive).TotalSeconds > InactivityTimeoutSeconds)
                            {
                                Log($"No client connected for {InactivityTimeoutSeconds}s. Auto-terminating.");
                                return;
                            }
                        }
                    }

                    if (ct.IsCancellationRequested) break;

                    Log("Client successfully connected to NamedPipe!");
                    lastClientActive = DateTime.UtcNow;

                    using var reader = new StreamReader(pipeServer, Encoding.UTF8, false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(pipeServer, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

                    using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var readCommandTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!clientCts.IsCancellationRequested && pipeServer.IsConnected)
                            {
                                string? line = await reader.ReadLineAsync(clientCts.Token);
                                if (line == null) break;
                                if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase) ||
                                    line.Equals("STOP", StringComparison.OrdinalIgnoreCase))
                                {
                                    Log("Received QUIT command from client.");
                                    _cts.Cancel();
                                    break;
                                }
                                else if (line.StartsWith("SET_GPU_POWER:", StringComparison.OrdinalIgnoreCase))
                                {
                                    string part = line.Substring("SET_GPU_POWER:".Length).Trim();
                                    if (int.TryParse(part, out int watts) && watts > 0)
                                    {
                                        ExecuteNvidiaSmiPower(watts);
                                    }
                                }
                                else if (line.StartsWith("RESET_GPU_POWER:", StringComparison.OrdinalIgnoreCase))
                                {
                                    string part = line.Substring("RESET_GPU_POWER:".Length).Trim();
                                    if (int.TryParse(part, out int watts) && watts > 0)
                                    {
                                        ExecuteNvidiaSmiPower(watts);
                                    }
                                }
                                else if (line.StartsWith("SET_GPU_FAN_MAX:", StringComparison.OrdinalIgnoreCase))
                                {
                                    string part = line.Substring("SET_GPU_FAN_MAX:".Length).Trim();
                                    if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct))
                                    {
                                        SetGpuFanSoftware(pct);
                                    }
                                }
                                else if (line.Equals("RESET_GPU_FAN", StringComparison.OrdinalIgnoreCase))
                                {
                                    ResetGpuFanSoftware();
                                }
                                else if (line.StartsWith("TEST_GPU_FAN", StringComparison.OrdinalIgnoreCase))
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        Log("Starting GPU fan spin test (50% PWM for 10 seconds)...");
                                        try
                                        {
                                            var activeControls = new List<IControl>();
                                            if (_computer != null)
                                            {
                                                foreach (var hw in _computer.Hardware)
                                                {
                                                    if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd)
                                                    {
                                                        foreach (var s in hw.Sensors)
                                                        {
                                                            if (s.SensorType == SensorType.Control && s.Control != null)
                                                            {
                                                                activeControls.Add(s.Control);
                                                                s.Control.SetSoftware(50f);
                                                                Log($"[TEST] Set GPU control '{s.Name}' to 50%");
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            await Task.Delay(10000);

                                            foreach (var c in activeControls)
                                            {
                                                try { c.SetDefault(); } catch { }
                                            }
                                            Log("[TEST] GPU fan spin test completed. Restored default BIOS curves.");
                                        }
                                        catch (Exception testEx)
                                        {
                                            Log($"[TEST] Spin test error: {testEx.Message}");
                                        }
                                    });
                                }
                            }
                        }
                        catch { }
                    });

                    // Streaming fan updates every 1000ms
                    while (!clientCts.IsCancellationRequested && pipeServer.IsConnected)
                    {
                        var snapshot = CollectFanSnapshot();
                        string json = JsonSerializer.Serialize(snapshot);
                        await writer.WriteLineAsync(json);
                        lastClientActive = DateTime.UtcNow;

                        await Task.Delay(1000, clientCts.Token);
                    }

                    Log("Client disconnected.");
                    clientCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Pipe error: {ex.Message}");
                    await Task.Delay(500, ct);
                }
            }
        }

        private static FanSnapshot CollectFanSnapshot()
        {
            var snapshot = new FanSnapshot { Timestamp = DateTime.UtcNow };

            if (_computer == null) return snapshot;

            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    UpdateHardwareTree(hw);
                    CollectSensors(hw, snapshot.Fans);
                }
            }
            catch (Exception ex)
            {
                Log($"CollectFanSnapshot error: {ex.Message}");
            }

            return snapshot;
        }

        private static void UpdateHardwareTree(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
            {
                UpdateHardwareTree(sub);
            }
        }

        private static void CollectSensors(IHardware hardware, List<FanMetricItem> fans)
        {
            var fanSensors = new List<ISensor>();
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Fan)
                {
                    fanSensors.Add(sensor);
                }
            }

            // Special handling for NVIDIA 3-fan graphics cards (e.g. RTX 3090, 3080, 4090, 4080, Trio, TUF, Strix):
            // Hardware PCBs have only 2 tachometer headers (Header 1 drives Fans 1 & 2, Header 2 drives Fan 3).
            // When 2 fan sensors are reported on a 3-fan GPU, expand to 3 fans matching physical cooler reality.
            if (hardware.HardwareType == HardwareType.GpuNvidia && fanSensors.Count == 2 &&
                (hardware.Name.Contains("3090") || hardware.Name.Contains("3080") ||
                 hardware.Name.Contains("4090") || hardware.Name.Contains("4080") ||
                 hardware.Name.Contains("Trio", StringComparison.OrdinalIgnoreCase) ||
                 hardware.Name.Contains("Trinity", StringComparison.OrdinalIgnoreCase) ||
                 hardware.Name.Contains("Strix", StringComparison.OrdinalIgnoreCase) ||
                 hardware.Name.Contains("TUF", StringComparison.OrdinalIgnoreCase)))
            {
                float val1 = fanSensors[0].Value ?? 0f;
                float val2 = fanSensors[1].Value ?? 0f;
                int rpm1 = Math.Max(0, (int)Math.Round(val1));
                int rpm2 = Math.Max(0, (int)Math.Round(val2));

                fans.Add(new FanMetricItem
                {
                    Id = fanSensors[0].Identifier.ToString() + "_1",
                    Name = "GPU Fan 1",
                    Hardware = hardware.Name,
                    HardwareType = hardware.HardwareType.ToString(),
                    Rpm = rpm1
                });
                fans.Add(new FanMetricItem
                {
                    Id = fanSensors[0].Identifier.ToString() + "_2",
                    Name = "GPU Fan 2",
                    Hardware = hardware.Name,
                    HardwareType = hardware.HardwareType.ToString(),
                    Rpm = rpm1
                });
                fans.Add(new FanMetricItem
                {
                    Id = fanSensors[1].Identifier.ToString() + "_3",
                    Name = "GPU Fan 3",
                    Hardware = hardware.Name,
                    HardwareType = hardware.HardwareType.ToString(),
                    Rpm = rpm2
                });
            }
            else
            {
                foreach (var sensor in fanSensors)
                {
                    float val = sensor.Value ?? 0f;
                    int rpm = (int)Math.Round(val);

                    string displayName = sensor.Name;
                    if (!displayName.Contains("Fan", StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = $"{sensor.Name} Fan";
                    }

                    fans.Add(new FanMetricItem
                    {
                        Id = sensor.Identifier.ToString(),
                        Name = displayName,
                        Hardware = hardware.Name,
                        HardwareType = hardware.HardwareType.ToString(),
                        Rpm = Math.Max(0, rpm)
                    });
                }
            }

            foreach (var sub in hardware.SubHardware)
            {
                CollectSensors(sub, fans);
            }
        }

        private static void SetGpuFanSoftware(float percent)
        {
            lock (_gpuControlLock)
            {
                try
                {
                    if (_computer == null) return;
                    percent = Math.Clamp(percent, 0f, 100f);
                    Log($"[GPU_FAN] Setting software control to {percent}%");
                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd)
                        {
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Control && s.Control != null)
                                {
                                    if (!_appliedGpuControls.Contains(s.Control))
                                    {
                                        _appliedGpuControls.Add(s.Control);
                                    }
                                    s.Control.SetSoftware(percent);
                                    Log($"[GPU_FAN] Control '{s.Name}' set to {percent}%");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[GPU_FAN] SetGpuFanSoftware error: {ex.Message}");
                }
            }
        }

        private static void ResetGpuFanSoftware()
        {
            lock (_gpuControlLock)
            {
                try
                {
                    Log("[GPU_FAN] Resetting GPU fan controls to hardware default");
                    foreach (var c in _appliedGpuControls)
                    {
                        try { c.SetDefault(); } catch { }
                    }
                    _appliedGpuControls.Clear();

                    if (_computer != null)
                    {
                        foreach (var hw in _computer.Hardware)
                        {
                            if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd)
                            {
                                foreach (var s in hw.Sensors)
                                {
                                    if (s.SensorType == SensorType.Control && s.Control != null)
                                    {
                                        try { s.Control.SetDefault(); } catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[GPU_FAN] ResetGpuFanSoftware error: {ex.Message}");
                }
            }
        }

        private static void ExecuteNvidiaSmiPower(int watts)
        {
            try
            {
                Log($"[GPU_POWER] Executing nvidia-smi -i 0 -pl {watts}");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = $"-i 0 -pl {watts}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    string outText = p.StandardOutput.ReadToEnd();
                    string errText = p.StandardError.ReadToEnd();
                    p.WaitForExit(3000);
                    Log($"[GPU_POWER] Result (exit code {p.ExitCode}): {outText.Trim()} {errText.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Log($"[GPU_POWER] ExecuteNvidiaSmiPower error: {ex.Message}");
            }
        }

        private static void Cleanup()
        {
            try
            {
                ResetGpuFanSoftware();
                _computer?.Close();
                _computer = null;
            }
            catch { }
        }
    }
}
