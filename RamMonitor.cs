using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RyzenQuietPro
{
    public class RamMonitor
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private readonly List<float> _history = new List<float>();
        private readonly int _maxHistory = 60;
        private readonly object _lock = new object();

        public float MemoryLoadPercent { get; private set; }
        public ulong TotalBytes { get; private set; }
        public ulong UsedBytes { get; private set; }
        public ulong AvailableBytes { get; private set; }
        public double TotalGb => TotalBytes / (1024.0 * 1024 * 1024);
        public double UsedGb => UsedBytes / (1024.0 * 1024 * 1024);

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock)
                {
                    return _history.ToArray();
                }
            }
        }

        public RamMonitor()
        {
            Sample();
            lock (_lock)
            {
                _history.Clear();
                for (int i = 0; i < _maxHistory; i++)
                {
                    _history.Add(MemoryLoadPercent);
                }
            }
        }

        public void Sample()
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    TotalBytes = mem.ullTotalPhys;
                    AvailableBytes = mem.ullAvailPhys;
                    UsedBytes = (TotalBytes >= AvailableBytes) ? (TotalBytes - AvailableBytes) : 0;
                    MemoryLoadPercent = Math.Clamp((float)mem.dwMemoryLoad, 0f, 100f);

                    lock (_lock)
                    {
                        _history.Add(MemoryLoadPercent);
                        while (_history.Count > _maxHistory)
                        {
                            _history.RemoveAt(0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"RamMonitor sample error: {ex.Message}");
            }
        }
    }
}
