using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RyzenQuietPro
{
    public class DiskDriveInfo
    {
        public string InstanceName { get; }
        public string DriveLetter { get; }
        public string FriendlyName { get; }
        public PerformanceCounter? Counter { get; }
        public float CurrentBytesPerSec { get; set; }
        public double CurrentMbPerSec => CurrentBytesPerSec / (1024.0 * 1024.0);

        public DiskDriveInfo(string instanceName, string driveLetter, string friendlyName, PerformanceCounter? counter)
        {
            InstanceName = instanceName;
            DriveLetter = driveLetter;
            FriendlyName = friendlyName;
            Counter = counter;
        }
    }

    public class DiskMonitor : IDisposable
    {
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

                foreach (var inst in instances)
                {
                    if (inst.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;

                    string letter = "";
                    int colonIdx = inst.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        char ch = inst[colonIdx - 1];
                        if (char.IsLetter(ch))
                        {
                            letter = $"{char.ToUpper(ch)}:";
                        }
                    }

                    if (string.IsNullOrEmpty(letter)) letter = inst;

                    string friendly = letter;
                    if (volumeLabels.TryGetValue(letter, out var volLabel) && !string.IsNullOrEmpty(volLabel))
                    {
                        friendly = $"{letter} ({volLabel})";
                    }

                    PerformanceCounter? driveCounter = null;
                    try
                    {
                        driveCounter = new PerformanceCounter("PhysicalDisk", "Disk Bytes/sec", inst);
                        driveCounter.NextValue();
                    }
                    catch { }

                    _drives.Add(new DiskDriveInfo(inst, letter, friendly, driveCounter));
                }

                Logger.Log($"DiskMonitor initialized. Discovered {_drives.Count} physical drive instance(s).");
            }
            catch (Exception ex)
            {
                Logger.Log($"DiskMonitor counter initialization error: {ex.Message}");
            }
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

                // Dynamic Peak Active Disk detection
                DiskDriveInfo? maxDrive = null;
                float maxBytes = 0f;

                foreach (var d in _drives)
                {
                    if (d.Counter != null)
                    {
                        try
                        {
                            float b = d.Counter.NextValue();
                            d.CurrentBytesPerSec = b;
                            if (b > maxBytes)
                            {
                                maxBytes = b;
                                maxDrive = d;
                            }
                        }
                        catch { }
                    }
                }

                float peakMb = (float)(maxBytes / (1024.0 * 1024.0));
                PeakDiskMbPerSec = peakMb;

                if (maxDrive != null && peakMb > 0.4f)
                {
                    PeakActiveDisk = maxDrive.FriendlyName;
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
                    d.Counter?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DiskMonitor dispose error: {ex.Message}");
            }
        }
    }
}