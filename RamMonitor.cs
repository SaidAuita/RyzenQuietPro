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

        [DllImport("kernel32.dll")]
        private static extern uint GetSystemFirmwareTable(uint Provider, uint TableID, IntPtr pBuffer, uint BufferSize);

        private readonly List<float> _history = new List<float>();
        private readonly int _maxHistory = 60;
        private readonly object _lock = new object();

        public float MemoryLoadPercent { get; private set; }
        public ulong TotalBytes { get; private set; }
        public ulong UsedBytes { get; private set; }
        public ulong AvailableBytes { get; private set; }
        public double TotalGb => TotalBytes / (1024.0 * 1024 * 1024);
        public double UsedGb => UsedBytes / (1024.0 * 1024 * 1024);
        public string MemoryType { get; private set; } = "";
        public int MemorySpeedMHz { get; private set; } = 0;
        public string MemorySpecs { get; private set; } = "";

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
            InitMemorySpecs();
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

        private void InitMemorySpecs()
        {
            try
            {
                var (mType, mSpeed) = QuerySmbiosMemoryInfo();
                if (!string.IsNullOrEmpty(mType) && mSpeed > 0)
                {
                    MemoryType = mType;
                    MemorySpeedMHz = mSpeed;
                    MemorySpecs = $"{mType}/{mSpeed}";
                }
                else if (!string.IsNullOrEmpty(mType))
                {
                    MemoryType = mType;
                    MemorySpecs = mType;
                }
                else if (mSpeed > 0)
                {
                    MemorySpeedMHz = mSpeed;
                    MemorySpecs = $"{mSpeed} MHz";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"InitMemorySpecs error: {ex.Message}");
            }
        }

        private static (string memType, int speedMhz) QuerySmbiosMemoryInfo()
        {
            try
            {
                const uint rsmbSig = 0x52534D42; // 'RSMB'
                uint size = GetSystemFirmwareTable(rsmbSig, 0, IntPtr.Zero, 0);
                if (size > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (GetSystemFirmwareTable(rsmbSig, 0, buf, size) == size)
                        {
                            byte[] data = new byte[size];
                            Marshal.Copy(buf, data, 0, (int)size);

                            int offset = 8;
                            while (offset < size - 4)
                            {
                                byte type = data[offset];
                                byte len = data[offset + 1];
                                if (len == 0) break;

                                if (type == 17 && len >= 0x16) // Type 17: Memory Device
                                {
                                    byte rawType = data[offset + 0x12];
                                    ushort speed = BitConverter.ToUInt16(data, offset + 0x15);
                                    ushort confSpeed = (len >= 0x22) ? BitConverter.ToUInt16(data, offset + 0x20) : speed;
                                    int effectiveSpeed = confSpeed > 0 ? confSpeed : speed;

                                    string typeStr = rawType switch
                                    {
                                        0x18 => "DDR3",
                                        0x1A => "DDR4",
                                        0x1E => "LPDDR4",
                                        0x22 => "DDR5",
                                        0x23 => "LPDDR5",
                                        0x24 => "HBM3",
                                        _ => effectiveSpeed >= 4400 ? "DDR5" : (effectiveSpeed >= 2133 ? "DDR4" : "RAM")
                                    };

                                    if (effectiveSpeed > 0)
                                    {
                                        return (typeStr, effectiveSpeed);
                                    }
                                }

                                offset += len;
                                while (offset < size - 1 && !(data[offset] == 0 && data[offset + 1] == 0))
                                {
                                    offset++;
                                }
                                offset += 2;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
            }
            catch { }

            return ("", 0);
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
