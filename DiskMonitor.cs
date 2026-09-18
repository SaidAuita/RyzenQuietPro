using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace RyzenQuietPro
{
    public class DiskDriveInfo
    {
        public string Id { get; }
        public string InstanceName { get; }
        public int PhysicalIndex { get; set; } = -1;
        public List<string> Partitions { get; set; } = new();
        public string DriveLetters => Partitions.Count > 0 ? string.Join(", ", Partitions) : DriveLetter;
        public string DriveLetter { get; set; }
        public string Model { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public double SizeGb { get; set; } = 0;
        public string FriendlyName { get; set; }
        public Color Color { get; set; }

        public PerformanceCounter? Counter => CounterBytes;
        public PerformanceCounter? CounterBytes { get; }
        public PerformanceCounter? CounterTime { get; }
        public PerformanceCounter? CounterRead { get; }
        public PerformanceCounter? CounterWrite { get; }

        public float LoadPercent { get; set; }
        public float ReadMbPerSec { get; set; }
        public float WriteMbPerSec { get; set; }
        public float CurrentBytesPerSec { get; set; }
        public double CurrentMbPerSec => CurrentBytesPerSec / (1024.0 * 1024.0);

        private readonly List<float> _history = new();
        private readonly object _lock = new();

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock) return _history.ToArray();
            }
        }

        public void AddHistory(float val)
        {
            lock (_lock)
            {
                _history.Add(val);
                while (_history.Count > 60) _history.RemoveAt(0);
            }
        }

        public DiskDriveInfo(
            string instanceName,
            string driveLetter,
            string friendlyName,
            PerformanceCounter? counterBytes,
            PerformanceCounter? counterTime,
            PerformanceCounter? counterRead,
            PerformanceCounter? counterWrite,
            Color color)
        {
            InstanceName = instanceName;
            DriveLetter = driveLetter;
            FriendlyName = friendlyName;
            CounterBytes = counterBytes;
            CounterTime = counterTime;
            CounterRead = counterRead;
            CounterWrite = counterWrite;
            Color = color;

            Id = instanceName;
            int spaceIdx = instanceName.IndexOf(' ');
            if (spaceIdx > 0 && int.TryParse(instanceName[..spaceIdx], out int idx))
            {
                PhysicalIndex = idx;
                Id = $"Disk_{idx}";
            }

            if (!string.IsNullOrEmpty(driveLetter))
            {
                Partitions.Add(driveLetter);
            }

            lock (_lock)
            {
                for (int i = 0; i < 60; i++) _history.Add(0f);
            }
        }
    }

    public class DiskMonitor : IDisposable
    {
        public static readonly Color[] DiskColors = new Color[]
        {
            Color.FromArgb(6, 182, 212),   // Cyan (#0)
            Color.FromArgb(249, 115, 22),  // Orange (#1)
            Color.FromArgb(168, 85, 247),  // Purple (#2)
            Color.FromArgb(34, 197, 94),   // Green (#3)
            Color.FromArgb(234, 179, 8),   // Amber/Gold (#4)
            Color.FromArgb(236, 72, 153),  // Pink (#5)
            Color.FromArgb(56, 189, 248),  // Sky Blue (#6)
            Color.FromArgb(244, 63, 94),   // Rose (#7)
            Color.FromArgb(20, 184, 166),  // Teal (#8)
            Color.FromArgb(163, 230, 53),  // Lime (#9)
            Color.FromArgb(99, 102, 241),  // Indigo (#10)
            Color.FromArgb(251, 146, 60),  // Light Orange (#11)
        };

        private PerformanceCounter? _totalTimeCounter;
        private PerformanceCounter? _readBytesCounter;
        private PerformanceCounter? _writeBytesCounter;
        private readonly List<DiskDriveInfo> _drives = new();
        private readonly List<float> _history = new();
        private readonly object _lock = new();

        public float DiskLoadPercent { get; private set; }
        public float ReadMbPerSec { get; private set; }
        public float WriteMbPerSec { get; private set; }
        public float TotalMbPerSec => ReadMbPerSec + WriteMbPerSec;
        public string PeakActiveDisk { get; private set; } = "C: (Idle)";
        public float PeakDiskMbPerSec { get; private set; } = 0f;
        public int DriveCount => _drives.Count;
        public IReadOnlyList<DiskDriveInfo> Drives => _drives;

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock) return _history.ToArray();
            }
        }

        public DiskMonitor()
        {
            lock (_lock)
            {
                for (int i = 0; i < 60; i++) _history.Add(0f);
            }

            InitializeCounters();
            _ = Task.Run(EnrichWithWmiAsync);
            Sample();
        }

        private void InitializeCounters()
        {
            try
            {
                _totalTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                _readBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _writeBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

                try { _totalTimeCounter.NextValue(); } catch { }
                try { _readBytesCounter.NextValue(); } catch { }
                try { _writeBytesCounter.NextValue(); } catch { }

                var cat = new PerformanceCounterCategory("PhysicalDisk");
                string[] instances = cat.GetInstanceNames();

                var volumeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady)
                        {
                            string letter = drive.Name.TrimEnd('\\', '/');
                            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : drive.VolumeLabel;
                            volumeLabels[letter] = label;
                        }
                    }
                }
                catch { }

                // Sort instances by physical index
                var sortedInstances = instances
                    .Where(inst => !inst.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(inst =>
                    {
                        int sp = inst.IndexOf(' ');
                        return sp > 0 && int.TryParse(inst[..sp], out int n) ? n : 999;
                    })
                    .ToList();

                int colorIdx = 0;
                foreach (var inst in sortedInstances)
                {
                    var letters = new List<string>();
                    int colonIdx = inst.IndexOf(':');
                    while (colonIdx > 0)
                    {
                        char ch = inst[colonIdx - 1];
                        if (char.IsLetter(ch))
                        {
                            string ltr = $"{char.ToUpper(ch)}:";
                            if (!letters.Contains(ltr)) letters.Add(ltr);
                        }
                        colonIdx = inst.IndexOf(':', colonIdx + 1);
                    }

                    string primaryLetter = letters.Count > 0 ? letters[0] : inst;
                    string friendly = primaryLetter;
                    if (letters.Count > 1)
                    {
                        friendly = string.Join(", ", letters);
                    }
                    else if (volumeLabels.TryGetValue(primaryLetter, out var volLabel) && !string.IsNullOrEmpty(volLabel))
                    {
                        friendly = $"{primaryLetter} ({volLabel})";
                    }

                    PerformanceCounter? cBytes = null;
                    PerformanceCounter? cTime = null;
                    PerformanceCounter? cRead = null;
                    PerformanceCounter? cWrite = null;

                    try
                    {
                        cBytes = new PerformanceCounter("PhysicalDisk", "Disk Bytes/sec", inst);
                        cBytes.NextValue();
                    }
                    catch { }

                    try
                    {
                        cTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", inst);
                        cTime.NextValue();
                    }
                    catch { }

                    try
                    {
                        cRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", inst);
                        cRead.NextValue();
                    }
                    catch { }

                    try
                    {
                        cWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", inst);
                        cWrite.NextValue();
                    }
                    catch { }

                    Color color = DiskColors[colorIdx % DiskColors.Length];
                    colorIdx++;

                    var driveInfo = new DiskDriveInfo(inst, primaryLetter, friendly, cBytes, cTime, cRead, cWrite, color);
                    if (letters.Count > 0)
                    {
                        driveInfo.Partitions = letters;
                    }
                    _drives.Add(driveInfo);
                }

                Logger.Log($"DiskMonitor initialized. Discovered {_drives.Count} physical drive instance(s).");
            }
            catch (Exception ex)
            {
                Logger.Log($"DiskMonitor counter initialization error: {ex.Message}");
            }
        }

        private async Task EnrichWithWmiAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                        foreach (ManagementObject drive in searcher.Get())
                        {
                            int index = -1;
                            if (drive["Index"] != null)
                            {
                                index = Convert.ToInt32(drive["Index"]);
                            }

                            string model = drive["Model"]?.ToString()?.Trim() ?? "";
                            string rawMfg = drive["Manufacturer"]?.ToString()?.Trim() ?? "";
                            ulong sizeBytes = 0;
                            if (drive["Size"] != null)
                            {
                                _ = ulong.TryParse(drive["Size"].ToString(), out sizeBytes);
                            }

                            string mfg = DetermineManufacturer(model, rawMfg);
                            double sizeGb = Math.Round(sizeBytes / (1024.0 * 1024.0 * 1024.0), 1);

                            // Find partitions
                            var partitionLetters = new List<string>();
                            try
                            {
                                foreach (ManagementObject partition in drive.GetRelated("Win32_DiskPartition"))
                                {
                                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                                    {
                                        string devId = logical["DeviceID"]?.ToString()?.Trim() ?? "";
                                        if (!string.IsNullOrEmpty(devId) && !partitionLetters.Contains(devId))
                                        {
                                            partitionLetters.Add(devId);
                                        }
                                    }
                                }
                            }
                            catch { }

                            // Match with existing drive info
                            var target = _drives.FirstOrDefault(d => d.PhysicalIndex == index);
                            if (target != null)
                            {
                                target.Model = model;
                                target.Manufacturer = mfg;
                                target.SizeGb = sizeGb;
                                if (partitionLetters.Count > 0)
                                {
                                    target.Partitions = partitionLetters;
                                }

                                string ltrStr = target.DriveLetters;
                                string prefix = !string.IsNullOrEmpty(ltrStr) ? $"[{ltrStr}] " : "";
                                string brand = !string.IsNullOrEmpty(mfg) && !model.StartsWith(mfg, StringComparison.OrdinalIgnoreCase)
                                    ? $"{mfg} " : "";
                                target.FriendlyName = $"{prefix}{brand}{model}".Trim();
                            }
                        }

                        Logger.Log($"DiskMonitor WMI enrichment completed for {_drives.Count} drives.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"DiskMonitor WMI enrichment error: {ex.Message}");
                    }
                });
            }
            catch { }
        }

        private static string DetermineManufacturer(string model, string rawMfg)
        {
            if (!string.IsNullOrEmpty(rawMfg) &&
                !rawMfg.Contains("standard", StringComparison.OrdinalIgnoreCase) &&
                !rawMfg.Contains("microsoft", StringComparison.OrdinalIgnoreCase))
            {
                return rawMfg;
            }

            string upper = model.ToUpperInvariant();
            if (upper.Contains("SAMSUNG")) return "Samsung";
            if (upper.Contains("TOSHIBA")) return "Toshiba";
            if (upper.Contains("WDC") || upper.Contains("WESTERN DIGITAL") || upper.StartsWith("WD")) return "Western Digital";
            if (upper.Contains("LEXAR")) return "Lexar";
            if (upper.Contains("KINGSTON")) return "Kingston";
            if (upper.Contains("CRUCIAL") || upper.Contains("MICRON")) return "Crucial";
            if (upper.Contains("SEAGATE") || upper.StartsWith("ST")) return "Seagate";
            if (upper.Contains("INTEL")) return "Intel";
            if (upper.Contains("HYNIX")) return "SK Hynix";
            if (upper.Contains("SANDISK")) return "SanDisk";
            if (upper.Contains("CORSAIR")) return "Corsair";
            if (upper.Contains("ADATA")) return "ADATA";

            int sp = model.IndexOf(' ');
            return sp > 0 ? model[..sp] : model;
        }

        public void Sample()
        {
            try
            {
                float timeVal = 0f;
                float readVal = 0f;
                float writeVal = 0f;

                if (_totalTimeCounter != null)
                {
                    try { timeVal = _totalTimeCounter.NextValue(); } catch { }
                }

                if (_readBytesCounter != null)
                {
                    try { readVal = _readBytesCounter.NextValue(); } catch { }
                }

                if (_writeBytesCounter != null)
                {
                    try { writeVal = _writeBytesCounter.NextValue(); } catch { }
                }

                DiskLoadPercent = Math.Clamp(timeVal, 0f, 100f);
                ReadMbPerSec = (float)(readVal / (1024.0 * 1024.0));
                WriteMbPerSec = (float)(writeVal / (1024.0 * 1024.0));

                lock (_lock)
                {
                    _history.Add(DiskLoadPercent);
                    while (_history.Count > 60) _history.RemoveAt(0);
                }

                // Sample individual physical drives
                DiskDriveInfo? maxDrive = null;
                float maxBytes = 0f;

                foreach (var d in _drives)
                {
                    float dBytes = 0f;
                    float dRead = 0f;
                    float dWrite = 0f;
                    float dTime = 0f;

                    if (d.CounterBytes != null)
                    {
                        try { dBytes = d.CounterBytes.NextValue(); } catch { }
                    }

                    if (d.CounterTime != null)
                    {
                        try { dTime = d.CounterTime.NextValue(); } catch { }
                    }

                    if (d.CounterRead != null)
                    {
                        try { dRead = d.CounterRead.NextValue(); } catch { }
                    }

                    if (d.CounterWrite != null)
                    {
                        try { dWrite = d.CounterWrite.NextValue(); } catch { }
                    }

                    d.CurrentBytesPerSec = dBytes;
                    d.ReadMbPerSec = (float)(dRead / (1024.0 * 1024.0));
                    d.WriteMbPerSec = (float)(dWrite / (1024.0 * 1024.0));

                    // If % Disk Time is valid, use it; otherwise infer activity ratio from MB/s
                    if (dTime > 0)
                    {
                        d.LoadPercent = Math.Clamp(dTime, 0f, 100f);
                    }
                    else if (dBytes > 0)
                    {
                        float estLoad = Math.Clamp((float)(d.CurrentMbPerSec / 200.0 * 100.0), 1f, 100f);
                        d.LoadPercent = estLoad;
                    }
                    else
                    {
                        d.LoadPercent = 0f;
                    }

                    d.AddHistory(d.LoadPercent);

                    if (dBytes > maxBytes)
                    {
                        maxBytes = dBytes;
                        maxDrive = d;
                    }
                }

                float peakMb = (float)(maxBytes / (1024.0 * 1024.0));
                PeakDiskMbPerSec = peakMb;

                if (maxDrive != null && peakMb > 0.4f)
                {
                    string ltr = maxDrive.DriveLetters;
                    PeakActiveDisk = !string.IsNullOrEmpty(ltr) ? ltr : maxDrive.FriendlyName;
                }
                else
                {
                    PeakActiveDisk = "Idle";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DiskMonitor sample error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                _totalTimeCounter?.Dispose();
                _readBytesCounter?.Dispose();
                _writeBytesCounter?.Dispose();
                foreach (var d in _drives)
                {
                    d.CounterBytes?.Dispose();
                    d.CounterTime?.Dispose();
                    d.CounterRead?.Dispose();
                    d.CounterWrite?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DiskMonitor dispose error: {ex.Message}");
            }
        }
    }
}