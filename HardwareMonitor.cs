using System;
using System.Threading;

namespace RyzenQuietPro
{
    public class HardwareMonitor : IDisposable
    {
        public CpuMonitor Cpu { get; }
        public RamMonitor Ram { get; }
        public GpuMonitor Gpu { get; }
        public DiskMonitor Disk { get; }
        public ProcessMonitor Processes { get; }
        public FanMonitorClient Fans { get; }
        public GpuTuningManager GpuTuning { get; }
        public StopwatchManager Stopwatch { get; }

        private readonly AppSettings _settings;
        private System.Threading.Timer? _timer;
        private int _isSampling = 0;
        public event Action? MetricsUpdated;

        public float CurrentCpuPowerWatts => Fans.CpuPowerWatts > 0 ? Fans.CpuPowerWatts : Cpu.EstimatedPowerWatts;
        public float CurrentGpuPowerWatts => Gpu.GpuPowerWatts;

        public float GetTriggerPowerWatts(int source)
        {
            float cpu = CurrentCpuPowerWatts;
            float gpu = CurrentGpuPowerWatts;
            return source switch
            {
                1 => cpu,
                2 => gpu,
                3 => cpu + gpu,
                _ => Math.Max(cpu, gpu)
            };
        }

        public HardwareMonitor(AppSettings? settings = null)
        {
            var appSettings = settings ?? AppSettings.Load();
            _settings = appSettings;
            Cpu = new CpuMonitor();
            Ram = new RamMonitor();
            Gpu = new GpuMonitor();
            Disk = new DiskMonitor();
            Processes = new ProcessMonitor();
            Fans = new FanMonitorClient();
            GpuTuning = new GpuTuningManager(this, appSettings);
            Stopwatch = new StopwatchManager(appSettings);
        }

        public void Start(int intervalMs = 1000)
        {
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Sample(), null, intervalMs, intervalMs);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Sample()
        {
            if (Interlocked.CompareExchange(ref _isSampling, 1, 0) != 0)
            {
                return; // Prior tick is still executing, skip overlapping tick
            }

            try
            {
                Processes.HasActiveAppTriggers = Stopwatch.HasAppTriggers || Stopwatch.Items.Any(i => i.Profile.ShowOnCpuGraph && i.Profile.GraphScale != -1 && i.IsRunning);

                Cpu.Sample();
                Ram.Sample();
                Gpu.Sample();
                Disk.Sample();
                Processes.Sample();
                Fans.Sample();

                GpuTuning.CheckFailSafe(Gpu.GpuTemperatureC);

                float triggerPower = GetTriggerPowerWatts(_settings.StopwatchTriggerSource);
                Stopwatch.UpdateAutoTrigger(
                    triggerPower,
                    _settings.StopwatchAutoStartWatts,
                    _settings.StopwatchAutoStopWatts,
                    _settings.StopwatchHysteresisSeconds,
                    1.0f,
                    Processes
                );

                // Record CPU samples for stopwatches
                foreach (var sw in Stopwatch.Items)
                {
                    if (sw.Profile.ShowOnCpuGraph && sw.Profile.GraphScale != -1)
                    {
                        if (sw.IsRunning)
                        {
                            float cpuVal = !string.IsNullOrWhiteSpace(sw.Profile.TargetExe)
                                ? (float)Processes.GetAppCpuPercent(sw.Profile.TargetExe)
                                : Cpu.TotalCpuUsage;
                            sw.RecordCpuSample(cpuVal);
                        }
                        else if (sw.HasRecordedHistory)
                        {
                            sw.RecordCpuSample(0f);
                        }
                    }
                }

                MetricsUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log($"HardwareMonitor sample error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isSampling, 0);
            }
        }

        public void Dispose()
        {
            Stop();
            Cpu.Dispose();
            Gpu.Dispose();
            Disk.Dispose();
            Processes.Dispose();
            Fans.Dispose();
        }
    }
}
