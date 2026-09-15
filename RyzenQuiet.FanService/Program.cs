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
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Fan)
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
                else if (sensor.SensorType == SensorType.Control && sensor.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
                {
                    float val = sensor.Value ?? 0f;
                    fans.Add(new FanMetricItem
                    {
                        Id = sensor.Identifier.ToString(),
                        Name = $"{sensor.Name} (%)",
                        Hardware = hardware.Name,
                        HardwareType = hardware.HardwareType.ToString(),
                        Rpm = (int)Math.Round(val)
                    });
                }
            }

            foreach (var sub in hardware.SubHardware)
            {
                CollectSensors(sub, fans);
            }
        }

        private static void Cleanup()
        {
            try
            {
                _computer?.Close();
                _computer = null;
            }
            catch { }
        }
    }
}
