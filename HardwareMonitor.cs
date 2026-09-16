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

        private System.Threading.Timer? _timer;
        public event Action? MetricsUpdated;

        public HardwareMonitor(AppSettings? settings = null)
        {
            var appSettings = settings ?? AppSettings.Load();
            Cpu = new CpuMonitor();
            Ram = new RamMonitor();
            Gpu = new GpuMonitor();
            Disk = new DiskMonitor();
            Processes = new ProcessMonitor();
            Fans = new FanMonitorClient();
            GpuTuning = new GpuTuningManager(this, appSettings);
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
