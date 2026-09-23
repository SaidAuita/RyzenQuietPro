using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
        public double ReadMbPerSec { get; set; }
        public double WriteMbPerSec { get; set; }
        public double TotalDiskMbPerSec => ReadMbPerSec + WriteMbPerSec;
        public string ExePath { get; set; } = "";
        public Image? Icon { get; set; }
    }

    public class ProcessAppGroup
    {
        public string ExeName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public int ProcessCount { get; set; }
        public List<int> Pids { get; set; } = new();
        public double TotalCpuPercent { get; set; }
        public long TotalWorkingSetBytes { get; set; }
        public string RamFormatted { get; set; } = "";
        public Image? Icon { get; set; }
        public string ExePath { get; set; } = "";
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
            public long ReadTransferBytes;  // offset 232 (0xE8) ReadTransferCount
            public long WriteTransferBytes; // offset 240 (0xF0) WriteTransferCount
        }

        private readonly int _processorCount;
        private Dictionary<int, long> _prevCpuTimes = new();
        private Dictionary<int, (long read, long write)> _prevIoTimes = new();
        private DateTime _prevSampleTime = DateTime.MinValue;
        private readonly object _lock = new();
        private readonly object _sampleLock = new();

        private static readonly ConcurrentDictionary<string, (string Name, Image? Icon, string Path)> _detailsCache = new(StringComparer.OrdinalIgnoreCase);

        private List<ProcessMetric> _topProcesses = new();
        private List<ProcessMetric> _topDiskProcesses = new();
        private readonly Dictionary<string, double> _appCpuMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runningAppNames = new(StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled { get; set; } = false;
        public bool HasActiveAppTriggers { get; set; } = false;


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

        public IReadOnlyList<ProcessMetric> TopDiskProcesses
        {
            get
            {
                lock (_lock)
                {
                    return _topDiskProcesses.ToArray();
                }
            }
        }

        public ProcessMonitor()
        {
            _processorCount = Math.Max(1, Environment.ProcessorCount);
        }

        public void Sample()
        {
            if (!IsEnabled && !HasActiveAppTriggers)
            {
                lock (_lock)
                {
                    if (_topProcesses.Count > 0) _topProcesses.Clear();
                    if (_topDiskProcesses.Count > 0) _topDiskProcesses.Clear();
                    _appCpuMap.Clear();
                    _runningAppNames.Clear();
                }
                lock (_sampleLock)
                {
                    _prevCpuTimes = new Dictionary<int, long>();
                    _prevIoTimes = new Dictionary<int, (long read, long write)>();
                    _prevSampleTime = DateTime.MinValue;
                }
                return;
            }

            if (!Monitor.TryEnter(_sampleLock))
            {
                return; // Another thread is sampling processes, skip overlapping run
            }

            try
            {
                var now = DateTime.UtcNow;
                var currentSnapshots = QuerySystemProcesses();

                if (_prevSampleTime == DateTime.MinValue || _prevCpuTimes.Count == 0)
                {
                    // First sample: record baseline
                    _prevCpuTimes = new Dictionary<int, long>(currentSnapshots.Count);
                    _prevIoTimes = new Dictionary<int, (long read, long write)>(currentSnapshots.Count);
                    foreach (var s in currentSnapshots)
                    {
                        _prevCpuTimes[s.Pid] = s.TotalCpuTime;
                        _prevIoTimes[s.Pid] = (s.ReadTransferBytes, s.WriteTransferBytes);
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
                var ioCandidates = new List<(ProcessRawSnapshot snap, double rMb, double wMb, double totMb)>();
                var appCpuDeltas = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

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
                            if (!string.IsNullOrEmpty(snap.Name))
                            {
                                appCpuDeltas[snap.Name] = (appCpuDeltas.TryGetValue(snap.Name, out long ad) ? ad : 0) + delta;
                            }
                        }
                    }

                    if (_prevIoTimes.TryGetValue(snap.Pid, out var prevIo))
                    {
                        long rDelta = Math.Max(0, snap.ReadTransferBytes - prevIo.read);
                        long wDelta = Math.Max(0, snap.WriteTransferBytes - prevIo.write);
                        if (rDelta > 0 || wDelta > 0)
                        {
                            double rMb = (rDelta / elapsedSec) / (1024.0 * 1024.0);
                            double wMb = (wDelta / elapsedSec) / (1024.0 * 1024.0);
                            double totMb = rMb + wMb;
                            if (totMb >= 0.01) // at least 10 KB/s
                            {
                                ioCandidates.Add((snap, rMb, wMb, totMb));
                            }
                        }
                    }
                }

                var newTop = new List<ProcessMetric>();
                var newTopDisk = new List<ProcessMetric>();

                if (IsEnabled)
                {
                    // Sort CPU candidates by delta descending to find top 5
                    candidates.Sort((a, b) => {
                        long aDelta = a.TotalCpuTime - (_prevCpuTimes.TryGetValue(a.Pid, out var at) ? at : 0);
                        long bDelta = b.TotalCpuTime - (_prevCpuTimes.TryGetValue(b.Pid, out var bt) ? bt : 0);
                        return bDelta.CompareTo(aDelta);
                    });

                    int takeCount = Math.Min(5, candidates.Count);
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
                            ExePath = details.Path,
                            Icon = details.Icon
                        });
                    }

                    // Sort Disk IO candidates by throughput descending to find top 5
                    ioCandidates.Sort((a, b) => b.totMb.CompareTo(a.totMb));
                    int diskTakeCount = Math.Min(5, ioCandidates.Count);
                    for (int i = 0; i < diskTakeCount; i++)
                    {
                        var item = ioCandidates[i];
                        var details = ResolveProcessDetails(item.snap.Pid, item.snap.Name);
                        newTopDisk.Add(new ProcessMetric
                        {
                            Pid = item.snap.Pid,
                            ExeName = item.snap.Name,
                            FriendlyName = details.Name,
                            WorkingSetBytes = item.snap.WorkingSet,
                            RamFormatted = FormatRam(item.snap.WorkingSet),
                            ReadMbPerSec = item.rMb,
                            WriteMbPerSec = item.wMb,
                            ExePath = details.Path,
                            Icon = details.Icon
                        });
                    }
                }

                // Update baseline
                _prevCpuTimes = new Dictionary<int, long>(currentSnapshots.Count);
                _prevIoTimes = new Dictionary<int, (long read, long write)>(currentSnapshots.Count);
                foreach (var s in currentSnapshots)
                {
                    _prevCpuTimes[s.Pid] = s.TotalCpuTime;
                    _prevIoTimes[s.Pid] = (s.ReadTransferBytes, s.WriteTransferBytes);
                }
                _prevSampleTime = now;

                lock (_lock)
                {
                    _topProcesses = newTop;
                    _topDiskProcesses = newTopDisk;
                    _runningAppNames.Clear();
                    foreach (var s in currentSnapshots)
                    {
                        if (s.Pid != 0 && !string.IsNullOrEmpty(s.Name))
                            _runningAppNames.Add(s.Name);
                    }
                    _appCpuMap.Clear();
                    foreach (var kvp in appCpuDeltas)
                    {
                        double pct = Math.Clamp((double)kvp.Value / totalCapacityTime * 100.0, 0.0, 100.0);
                        _appCpuMap[kvp.Key] = pct;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ProcessMonitor sample error: {ex.Message}");
                _prevCpuTimes = new Dictionary<int, long>();
                _prevIoTimes = new Dictionary<int, (long read, long write)>();
                _prevSampleTime = DateTime.MinValue;
            }
            finally
            {
                Monitor.Exit(_sampleLock);
            }
        }

        public double GetAppCpuPercent(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return 0.0;
            lock (_lock)
            {
                return _appCpuMap.TryGetValue(exeName, out double val) ? val : 0.0;
            }
        }

        public bool IsAppRunning(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return false;
            lock (_lock)
            {
                return _runningAppNames.Contains(exeName);
            }
        }

        public List<ProcessAppGroup> GetRunningAppGroups()
        {
            var snapshots = QuerySystemProcesses();
            var groups = new Dictionary<string, ProcessAppGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in snapshots)
            {
                if (s.Pid == 0 || string.IsNullOrEmpty(s.Name)) continue;

                if (!groups.TryGetValue(s.Name, out var group))
                {
                    var details = ResolveProcessDetails(s.Pid, s.Name);
                    group = new ProcessAppGroup
                    {
                        ExeName = s.Name,
                        FriendlyName = details.Name,
                        ExePath = details.Path,
                        Icon = details.Icon,
                        ProcessCount = 0,
                        TotalWorkingSetBytes = 0
                    };
                    groups[s.Name] = group;
                }

                group.ProcessCount++;
                group.Pids.Add(s.Pid);
                group.TotalWorkingSetBytes += s.WorkingSet;
            }

            lock (_lock)
            {
                foreach (var g in groups.Values)
                {
                    g.TotalCpuPercent = _appCpuMap.TryGetValue(g.ExeName, out double cpu) ? cpu : 0.0;
                    g.RamFormatted = FormatRam(g.TotalWorkingSetBytes);
                }
            }

            var result = new List<ProcessAppGroup>(groups.Values);
            result.Sort((a, b) =>
            {
                int cmp = b.TotalCpuPercent.CompareTo(a.TotalCpuPercent);
                if (cmp != 0) return cmp;
                cmp = b.TotalWorkingSetBytes.CompareTo(a.TotalWorkingSetBytes);
                if (cmp != 0) return cmp;
                return string.Compare(a.FriendlyName, b.FriendlyName, StringComparison.OrdinalIgnoreCase);
            });

            return result;
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
                    long readBytes = Marshal.ReadInt64(currentPtr, 232);
                    long writeBytes = Marshal.ReadInt64(currentPtr, 240);

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
                        WorkingSet = workingSetPtr.ToInt64(),
                        ReadTransferBytes = readBytes,
                        WriteTransferBytes = writeBytes
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

        private static readonly Dictionary<string, string> _knownServiceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["service_process"] = "Acronis Backup Engine",
            ["mms"] = "Acronis Managed Machine",
            ["agent"] = "Acronis Remote Agent",
            ["TibMounterMonitor"] = "Acronis Tib Mounter",
            ["ExilandBackupService"] = "Exiland Backup Service",
            ["ExilandBackup"] = "Exiland Backup",
            ["SearchIndexer"] = "Windows Search Indexer",
            ["System"] = "Windows Kernel / Paging",
            ["System Idle Process"] = "System Idle",
            ["svchost"] = "Windows Service Host",
            ["TiWorker"] = "Windows Update Worker",
            ["TrustedInstaller"] = "Windows Modules Installer",
            ["MsMpEng"] = "Microsoft Defender Antivirus",
            ["vssvc"] = "Volume Shadow Copy Service",
            ["defrag"] = "Windows Drive Defragmenter",
            ["dfrgui"] = "Defragment & Optimize Drives",
            ["CompPkgSrv"] = "Component Package Support Server",
            ["taskhostw"] = "Host Process for Windows Tasks"
        };

        private static readonly string[] _knownServiceDirs = new[]
        {
            @"C:\Program Files (x86)\Common Files\Acronis\BackupAndRecovery\Common64",
            @"C:\Program Files (x86)\Common Files\Acronis\BackupAndRecovery\Common",
            @"C:\Program Files (x86)\Acronis\BackupAndRecovery",
            @"C:\Program Files\Common Files\Acronis\BackupAndRecovery\Common64",
            @"C:\Exiland Backup Professional",
            @"C:\Windows\System32"
        };

        private static (string Name, Image? Icon, string Path) ResolveProcessDetails(int pid, string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return ("Unknown", null, "");

            if (_detailsCache.TryGetValue(exeName, out var cached))
            {
                return cached;
            }

            string cleanName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? exeName[..^4]
                : exeName;

            string friendlyName = cleanName;
            if (_knownServiceNames.TryGetValue(cleanName, out var knownName))
            {
                friendlyName = knownName;
            }

            Image? iconImage = null;
            string foundPath = "";

            try
            {
                string? path = GetProcessPath(pid);
                if (string.IsNullOrEmpty(path))
                {
                    // Fallback to searching known locations if access was denied to system service
                    foreach (var dir in _knownServiceDirs)
                    {
                        string candidate = Path.Combine(dir, exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe");
                        if (File.Exists(candidate))
                        {
                            path = candidate;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(path))
                {
                    foundPath = path;
                    if (File.Exists(path))
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(fvi.FileDescription) && !_knownServiceNames.ContainsKey(cleanName))
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
            }
            catch
            {
                // Process exited or access denied
            }

            var result = (friendlyName, iconImage, foundPath);
            _detailsCache[exeName] = result;
            return result;
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
            foreach (var item in _detailsCache.Values)
            {
                item.Icon?.Dispose();
            }
            _detailsCache.Clear();
        }
    }
}
