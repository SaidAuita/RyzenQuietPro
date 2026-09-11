using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RyzenQuietPro
{
    public class ProcessMetric
    {
        public int Pid { get; set; }
        public string ExeName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public double CpuPercent { get; set; }
        public long WorkingSetBytes { get; set; }
        public string RamFormatted { get; set; } = "";
        public Image? Icon { get; set; }
    }

    public class ProcessMonitor : IDisposable
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            uint systemInformationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int SystemProcessInformation = 5;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private struct ProcessRawSnapshot
        {
            public int Pid;
            public string Name;
            public long TotalCpuTime; // Kernel + User (100ns units)
            public long WorkingSet;   // RAM bytes
        }

        private readonly int _processorCount;
        private readonly Dictionary<int, long> _prevCpuTimes = new();
        private DateTime _prevSampleTime = DateTime.MinValue;
        private readonly object _lock = new();

        private static readonly Dictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Image?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        private List<ProcessMetric> _topProcesses = new();

        public bool IsEnabled { get; set; } = false;

        public IReadOnlyList<ProcessMetric> TopProcesses
        {
            get
            {
                lock (_lock)
                {
                    return _topProcesses.ToArray();
                }
            }
        }

        public ProcessMonitor()
        {
            _processorCount = Math.Max(1, Environment.ProcessorCount);
        }

        public void Sample()
        {
            if (!IsEnabled)
            {
                lock (_lock)
                {
                    if (_topProcesses.Count > 0) _topProcesses.Clear();
                }
                _prevCpuTimes.Clear();
                _prevSampleTime = DateTime.MinValue;
                return;
            }

            try
            {
                var now = DateTime.UtcNow;
                var currentSnapshots = QuerySystemProcesses();

                if (_prevSampleTime == DateTime.MinValue || _prevCpuTimes.Count == 0)
                {
                    // First sample: record baseline
                    _prevCpuTimes.Clear();
                    foreach (var s in currentSnapshots)
                    {
                        _prevCpuTimes[s.Pid] = s.TotalCpuTime;
                    }
                    _prevSampleTime = now;
                    return;
                }

                double elapsedSec = (now - _prevSampleTime).TotalSeconds;
                if (elapsedSec <= 0.1) return; // Prevent divide-by-zero or erratic high rates

                long totalCapacityTime = (long)(elapsedSec * 10_000_000.0 * _processorCount);
                if (totalCapacityTime <= 0) return;

                var currentPids = new HashSet<int>(currentSnapshots.Count);
                var candidates = new List<ProcessRawSnapshot>();

                // Build candidate list of non-idle processes
                foreach (var snap in currentSnapshots)
                {
                    currentPids.Add(snap.Pid);
                    if (snap.Pid == 0) continue; // Skip System Idle Process

                    if (_prevCpuTimes.TryGetValue(snap.Pid, out long prevTime))
                    {
                        long delta = snap.TotalCpuTime - prevTime;
                        if (delta > 0)
                        {
                            candidates.Add(snap);
                        }
                    }
                }

                // Sort candidates by delta descending to find top 5
                candidates.Sort((a, b) => {
                    long aDelta = a.TotalCpuTime - (_prevCpuTimes.TryGetValue(a.Pid, out var at) ? at : 0);
                    long bDelta = b.TotalCpuTime - (_prevCpuTimes.TryGetValue(b.Pid, out var bt) ? bt : 0);
                    return bDelta.CompareTo(aDelta);
                });

                int takeCount = Math.Min(5, candidates.Count);
                var newTop = new List<ProcessMetric>(takeCount);

                for (int i = 0; i < takeCount; i++)
                {
                    var c = candidates[i];
                    long delta = c.TotalCpuTime - (_prevCpuTimes.TryGetValue(c.Pid, out var prev) ? prev : 0);
                    double cpuPct = Math.Clamp((double)delta / totalCapacityTime * 100.0, 0.0, 100.0);

                    var details = ResolveProcessDetails(c.Pid, c.Name);

                    newTop.Add(new ProcessMetric
                    {
                        Pid = c.Pid,
                        ExeName = c.Name,
                        FriendlyName = details.Name,
                        CpuPercent = cpuPct,
                        WorkingSetBytes = c.WorkingSet,
                        RamFormatted = FormatRam(c.WorkingSet),
                        Icon = details.Icon
                    });
                }

                // Update baseline
                _prevCpuTimes.Clear();
                foreach (var s in currentSnapshots)
                {
                    _prevCpuTimes[s.Pid] = s.TotalCpuTime;
                }
                _prevSampleTime = now;

                lock (_lock)
                {
                    _topProcesses = newTop;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ProcessMonitor sample error: {ex.Message}");
            }
        }

        private static List<ProcessRawSnapshot> QuerySystemProcesses()
        {
            var list = new List<ProcessRawSnapshot>(350);
            uint bufferSize = 512 * 1024; // 512 KB
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);

            try
            {
                int status;
                while ((status = NtQuerySystemInformation(SystemProcessInformation, buffer, bufferSize, out uint returnLength)) == -1073741820) // STATUS_INFO_LENGTH_MISMATCH
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = Math.Max(bufferSize * 2, returnLength + 32768);
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                }

                if (status != 0) return list;

                IntPtr currentPtr = buffer;
                while (true)
                {
                    int nextOffset = Marshal.ReadInt32(currentPtr, 0);
                    long userTime = Marshal.ReadInt64(currentPtr, 40);
                    long kernelTime = Marshal.ReadInt64(currentPtr, 48);

                    ushort nameLength = (ushort)Marshal.ReadInt16(currentPtr, 56);
                    IntPtr nameBuffer = Marshal.ReadIntPtr(currentPtr, 64);
                    IntPtr pidPtr = Marshal.ReadIntPtr(currentPtr, 80);
                    IntPtr workingSetPtr = Marshal.ReadIntPtr(currentPtr, 144);

                    int pid = pidPtr.ToInt32();
                    string name;
                    if (nameBuffer != IntPtr.Zero && nameLength > 0)
                    {
                        name = Marshal.PtrToStringUni(nameBuffer, nameLength / 2) ?? "";
                    }
                    else
                    {
                        name = (pid == 0) ? "System Idle Process" : ((pid == 4) ? "System" : "Unknown");
                    }

                    list.Add(new ProcessRawSnapshot
                    {
                        Pid = pid,
                        Name = name,
                        TotalCpuTime = userTime + kernelTime,
                        WorkingSet = workingSetPtr.ToInt64()
                    });

                    if (nextOffset == 0) break;
                    currentPtr = IntPtr.Add(currentPtr, nextOffset);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return list;
        }

        private static (string Name, Image? Icon) ResolveProcessDetails(int pid, string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return ("Unknown", null);

            string cleanName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? exeName[..^4]
                : exeName;

            lock (_nameCache)
            {
                if (_nameCache.TryGetValue(exeName, out var cachedName) && _iconCache.TryGetValue(exeName, out var cachedIcon))
                {
                    return (cachedName, cachedIcon);
                }
            }

            string friendlyName = cleanName;
            Image? iconImage = null;

            try
            {
                string? path = GetProcessPath(pid);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                    {
                        friendlyName = fvi.FileDescription.Trim();
                    }

                    try
                    {
                        using var icon = Icon.ExtractAssociatedIcon(path);
                        if (icon != null)
                        {
                            iconImage = icon.ToBitmap();
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                // Process exited or access denied
            }

            lock (_nameCache)
            {
                _nameCache[exeName] = friendlyName;
                _iconCache[exeName] = iconImage;
            }

            return (friendlyName, iconImage);
        }

        private static string? GetProcessPath(int pid)
        {
            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
            finally
            {
                CloseHandle(hProc);
            }
            return null;
        }

        public static string FormatRam(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024.0)
            {
                return $"{mb / 1024.0:F1} GB";
            }
            return $"{mb:F0} MB";
        }

        public void Dispose()
        {
            lock (_iconCache)
            {
                foreach (var img in _iconCache.Values)
                {
                    img?.Dispose();
                }
                _iconCache.Clear();
                _nameCache.Clear();
            }
        }
    }
}
