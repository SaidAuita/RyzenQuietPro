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
            Stopwatch = new StopwatchManager
            {
                IsAutoArmed = appSettings.StopwatchAutoStartEnabled
            };
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
            try
            {
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
                    1.0f
                );

                MetricsUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log($"HardwareMonitor sample error: {ex.Message}");
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
