using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace RyzenQuietPro
{
    public class CpuMonitor : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(LASTINPUTINFO));
            [MarshalAs(UnmanagedType.U4)]
            public int cbSize;
            [MarshalAs(UnmanagedType.U4)]
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        private readonly int _processorCount;
        private PerformanceCounter? _totalCounter;
        private PerformanceCounter? _freqCounter;
        private PerformanceCounter? _perfPerformanceCounter;
        private PerformanceCounter[]? _coreCounters;

        private long _prevIdle = 0;
        private long _prevKernel = 0;
        private long _prevUser = 0;

        private readonly List<float> _history = new List<float>();
        private readonly int _maxHistory = 60;
        private readonly object _lock = new object();

        public float TotalCpuUsage { get; private set; }
        public float CurrentFrequencyMHz { get; private set; }
        public float[] CoreUsages { get; private set; }
        public float EstimatedPowerWatts
        {
            get
            {
                float usageRatio = Math.Clamp(TotalCpuUsage / 100f, 0f, 1f);
                float freqRatio = CurrentFrequencyMHz > 1000 ? Math.Clamp(CurrentFrequencyMHz / 3500f, 0.6f, 1.4f) : 1.0f;
                return (14f + (75f * usageRatio * freqRatio));
            }
        }
        public int CoreCount => _processorCount;
        public string CpuName { get; }
        public string CpuShortName { get; }

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

        public CpuMonitor()
        {
            _processorCount = Environment.ProcessorCount;
            CoreUsages = new float[_processorCount];
            CpuName = QueryCpuName();
            CpuShortName = CleanCpuName(CpuName);

            lock (_lock)
            {
                for (int i = 0; i < _maxHistory; i++) _history.Add(0f);
            }

            GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
            InitializeCounters();
        }

        private void InitializeCounters()
        {
            try
            {
                _totalCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
                _freqCounter = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total");
                _perfPerformanceCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");

                _coreCounters = new PerformanceCounter[_processorCount];
                for (int i = 0; i < _processorCount; i++)
                {
                    _coreCounters[i] = new PerformanceCounter("Processor Information", "% Processor Utility", $"0,{i}");
                }

                // Prime initial values
                _totalCounter.NextValue();
                _freqCounter.NextValue();
                _perfPerformanceCounter.NextValue();
                for (int i = 0; i < _processorCount; i++)
                {
                    _coreCounters[i].NextValue();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"CpuMonitor counters init error: {ex.Message}");
            }
        }

        public void Sample()
        {
            try
            {
                // 1. Total CPU load
                if (_totalCounter != null)
                {
                    float total = _totalCounter.NextValue();
                    TotalCpuUsage = Math.Clamp(total, 0f, 100f);
                }
                else
                {
                    if (GetSystemTimes(out long currIdle, out long currKernel, out long currUser))
                    {
                        long kDelta = currKernel - _prevKernel;
                        long uDelta = currUser - _prevUser;
                        long iDelta = currIdle - _prevIdle;
                        long total = kDelta + uDelta;
                        if (total > 0)
                        {
                            TotalCpuUsage = Math.Clamp((float)((total - iDelta) * 100.0 / total), 0f, 100f);
                        }
                        _prevKernel = currKernel;
                        _prevUser = currUser;
                        _prevIdle = currIdle;
                    }
                }

                // 2. Frequency MHz
                if (_freqCounter != null && _perfPerformanceCounter != null)
                {
                    float baseMhz = _freqCounter.NextValue();
                    float perfPct = _perfPerformanceCounter.NextValue();
                    if (baseMhz > 0 && perfPct > 0)
                    {
                        CurrentFrequencyMHz = baseMhz * (perfPct / 100f);
                    }
                    else
                    {
                        CurrentFrequencyMHz = baseMhz;
                    }
                }
                else if (_freqCounter != null)
                {
                    CurrentFrequencyMHz = _freqCounter.NextValue();
                }

                // 3. Per-core CPU load
                if (_coreCounters != null)
                {
                    for (int i = 0; i < _processorCount; i++)
                    {
                        float coreVal = _coreCounters[i].NextValue();
                        CoreUsages[i] = Math.Clamp(coreVal, 0f, 100f);
                    }
                }

                lock (_lock)
                {
                    _history.Add(TotalCpuUsage);
                    while (_history.Count > _maxHistory)
                    {
                        _history.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"CpuMonitor sample error: {ex.Message}");
            }
        }

        private static string QueryCpuName()
        {
            try
            {
                var name = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null);
                if (name != null)
                {
                    string s = name.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return "Multi-Core Processor";
        }

        public static string CleanCpuName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "CPU";
            string s = raw.Trim();
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace("Processor", "").Trim();
            int idx = s.IndexOf("with Radeon", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) s = s.Substring(0, idx).Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }

        public static TimeSpan GetIdleTime()
        {
            var lii = new LASTINPUTINFO { cbSize = LASTINPUTINFO.SizeOf };
            if (GetLastInputInfo(ref lii))
            {
                uint idleTicks = (uint)Environment.TickCount - lii.dwTime;
                return TimeSpan.FromMilliseconds(idleTicks);
            }
            return TimeSpan.Zero;
        }

        public void Dispose()
        {
            _totalCounter?.Dispose();
            _freqCounter?.Dispose();
            _perfPerformanceCounter?.Dispose();
            if (_coreCounters != null)
            {
                foreach (var c in _coreCounters) c?.Dispose();
            }
        }
    }
}
