using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RyzenQuiet.Mac
{
    public class MacProcessItem
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuPercent { get; set; }
        public string RamFormatted { get; set; } = string.Empty;
    }

    public class MacCpuMonitor
    {
        public string CpuName { get; private set; } = "Intel Core";
        public int PhysicalCores { get; private set; } = 2;
        public int LogicalCores { get; private set; } = 4;
        public double BaseGhz { get; private set; } = 2.60;
        public double CurrentGhz { get; private set; } = 2.60;
        public float TotalPercent { get; private set; } = 0f;
        public float[] CoreLoads { get; private set; } = Array.Empty<float>();

        private readonly List<float> _history = new();
        private readonly object _lock = new();

        private uint _processorCount = 0;
        private uint[][]? _prevTicks;

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock) return _history.ToArray();
            }
        }

        public List<MacProcessItem> TopProcesses { get; private set; } = new();

        public MacCpuMonitor()
        {
            lock (_lock)
            {
                for (int i = 0; i < 60; i++) _history.Add(0f);
            }

            InitHardwareInfo();
            Sample();
        }

        private void InitHardwareInfo()
        {
            string brand = MacNative.GetSysctlString("machdep.cpu.brand_string", "Intel Processor");
            CpuName = CleanCpuName(brand);

            int phys = MacNative.GetSysctlInt32("hw.physicalcpu", 2);
            int log = MacNative.GetSysctlInt32("hw.logicalcpu", 4);
            PhysicalCores = Math.Max(1, phys);
            LogicalCores = Math.Max(1, log);
            CoreLoads = new float[LogicalCores];

            long freqHz = MacNative.GetSysctlInt64("hw.cpufrequency", 0);
            if (freqHz > 0)
            {
                BaseGhz = Math.Round(freqHz / 1_000_000_000.0, 2);
                CurrentGhz = BaseGhz;
            }
            else
            {
                // Try parse from brand string, e.g. "@ 2.60GHz"
                int atIdx = brand.IndexOf('@');
                if (atIdx >= 0)
                {
                    string sub = brand.Substring(atIdx + 1).Replace("GHz", "").Trim();
                    if (double.TryParse(sub, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedGhz))
                    {
                        BaseGhz = parsedGhz;
                        CurrentGhz = BaseGhz;
                    }
                }
            }
        }

        private static string CleanCpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Intel Core";
            string s = name.Replace("(R)", "")
                           .Replace("(TM)", "")
                           .Replace("CPU", "")
                           .Trim();
            int atIdx = s.IndexOf('@');
            if (atIdx > 0) s = s.Substring(0, atIdx).Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }

        public void Sample()
        {
            try
            {
                IntPtr host = MacNative.mach_host_self();
                int ret = MacNative.host_processor_info(
                    host,
                    MacNative.PROCESSOR_CPU_LOAD_INFO,
                    out uint procCount,
                    out IntPtr infoPtr,
                    out uint infoCount);

                if (ret == 0 && infoPtr != IntPtr.Zero && procCount > 0)
                {
                    _processorCount = procCount;
                    if (CoreLoads.Length != procCount)
                    {
                        CoreLoads = new float[procCount];
                    }

                    int[] raw = new int[procCount * MacNative.CPU_STATE_MAX];
                    Marshal.Copy(infoPtr, raw, 0, raw.Length);
                    MacNative.vm_deallocate(MacNative.mach_task_self(), infoPtr, (nuint)(infoCount * sizeof(int)));

                    uint[][] currentTicks = new uint[procCount][];
                    for (int i = 0; i < procCount; i++)
                    {
                        currentTicks[i] = new uint[MacNative.CPU_STATE_MAX];
                        for (int s = 0; s < MacNative.CPU_STATE_MAX; s++)
                        {
                            currentTicks[i][s] = (uint)raw[i * MacNative.CPU_STATE_MAX + s];
                        }
                    }

                    if (_prevTicks != null && _prevTicks.Length == procCount)
                    {
                        float sumUsage = 0f;
                        for (int i = 0; i < procCount; i++)
                        {
                            uint user = currentTicks[i][MacNative.CPU_STATE_USER] - _prevTicks[i][MacNative.CPU_STATE_USER];
                            uint sys = currentTicks[i][MacNative.CPU_STATE_SYSTEM] - _prevTicks[i][MacNative.CPU_STATE_SYSTEM];
                            uint nice = currentTicks[i][MacNative.CPU_STATE_NICE] - _prevTicks[i][MacNative.CPU_STATE_NICE];
                            uint idle = currentTicks[i][MacNative.CPU_STATE_IDLE] - _prevTicks[i][MacNative.CPU_STATE_IDLE];
                            uint total = user + sys + nice + idle;

                            float corePercent = 0f;
                            if (total > 0)
                            {
                                corePercent = ((float)(user + sys + nice) / total) * 100f;
                            }
                            CoreLoads[i] = Math.Clamp(corePercent, 0f, 100f);
                            sumUsage += CoreLoads[i];
                        }

                        TotalPercent = Math.Clamp(sumUsage / procCount, 0f, 100f);
                    }

                    _prevTicks = currentTicks;
                }
            }
            catch { }

            lock (_lock)
            {
                _history.Add(TotalPercent);
                while (_history.Count > 60) _history.RemoveAt(0);
            }

            // Estimate dynamic frequency based on load
            CurrentGhz = Math.Round(BaseGhz + ((TotalPercent / 100.0) * (BaseGhz * 0.25)), 2);

            SampleTopProcesses();
        }

        private void SampleTopProcesses()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/ps",
                    Arguments = "-Ao pid,pcpu,pmem,comm -r",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(300);

                var list = new List<MacProcessItem>();
                string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < Math.Min(6, lines.Length); i++)
                {
                    string line = lines[i].Trim();
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[0], out int pid) &&
                            double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cpu))
                        {
                            string comm = parts[3];
                            int lastSlash = comm.LastIndexOf('/');
                            if (lastSlash >= 0) comm = comm.Substring(lastSlash + 1);

                            list.Add(new MacProcessItem
                            {
                                Pid = pid,
                                Name = comm,
                                CpuPercent = Math.Round(cpu, 1),
                                RamFormatted = $"{parts[2]}%"
                            });
                        }
                    }
                }

                TopProcesses = list;
            }
            catch { }
        }
    }
}
