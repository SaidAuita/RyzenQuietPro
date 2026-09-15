using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RyzenQuiet.Mac
{
    public class MacRamMonitor
    {
        public double TotalGb { get; private set; } = 8.0;
        public double UsedGb { get; private set; } = 0.0;
        public float UsagePercent { get; private set; } = 0f;
        public string MemorySpecs { get; private set; } = "DDR3/1600";

        private readonly List<float> _history = new();
        private readonly object _lock = new();
        private long _totalBytes = 8589934592;
        private const int PageSize = 4096;

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock) return _history.ToArray();
            }
        }

        public MacRamMonitor()
        {
            lock (_lock)
            {
                for (int i = 0; i < 60; i++) _history.Add(0f);
            }

            InitMemorySpecs();
            Sample();
        }

        private void InitMemorySpecs()
        {
            long memBytes = MacNative.GetSysctlInt64("hw.memsize", 0);
            if (memBytes > 0)
            {
                _totalBytes = memBytes;
                TotalGb = Math.Round(_totalBytes / (1024.0 * 1024.0 * 1024.0), 1);
            }

            // Detect memory type from system_profiler
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/sbin/system_profiler",
                    Arguments = "SPMemoryDataType -detailLevel mini",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(400);

                    string type = "DDR3";
                    string speed = "1600";

                    foreach (var line in output.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
                        {
                            type = trimmed.Substring(5).Trim();
                        }
                        else if (trimmed.StartsWith("Speed:", StringComparison.OrdinalIgnoreCase))
                        {
                            speed = trimmed.Substring(6).Replace("MHz", "").Trim();
                        }
                    }

                    MemorySpecs = $"{type}/{speed}";
                }
            }
            catch
            {
                MemorySpecs = "DDR3/1600";
            }
        }

        public void Sample()
        {
            try
            {
                // vm_statistics64_data_t is 38 integers (152 bytes)
                uint count = (uint)MacNative.HOST_VM_INFO64_COUNT;
                int bufSize = (int)count * sizeof(int);
                IntPtr buf = Marshal.AllocHGlobal(bufSize);

                try
                {
                    IntPtr host = MacNative.mach_host_self();
                    int ret = MacNative.host_statistics64(host, MacNative.HOST_VM_INFO64, buf, ref count);
                    if (ret == 0)
                    {
                        // Offsets in vm_statistics64:
                        // free_count: index 0
                        // active_count: index 1
                        // inactive_count: index 2
                        // wire_count: index 3
                        // ...
                        // compressor_page_count: index 22
                        int[] vm = new int[count];
                        Marshal.Copy(buf, vm, 0, (int)count);

                        long activePages = (uint)vm[1];
                        long wirePages = (uint)vm[3];
                        long compPages = vm.Length > 22 ? (uint)vm[22] : 0;

                        long usedBytes = (activePages + wirePages + compPages) * PageSize;
                        UsedGb = Math.Round(usedBytes / (1024.0 * 1024.0 * 1024.0), 1);
                        UsagePercent = (float)Math.Clamp((UsedGb / Math.Max(1.0, TotalGb)) * 100.0, 0.0, 100.0);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch
            {
                UsedGb = Math.Round(TotalGb * 0.45, 1);
                UsagePercent = 45f;
            }

            lock (_lock)
            {
                _history.Add(UsagePercent);
                while (_history.Count > 60) _history.RemoveAt(0);
            }
        }
    }
}
