using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace RyzenQuietPro
{
    public class StopwatchItem
    {
        private readonly Stopwatch _stopwatch = new();
        private TimeSpan _accumulated = TimeSpan.Zero;
        private float _powerHighDuration = 0f;
        private float _powerLowDuration = 0f;

        private readonly List<float?> _cpuHistory = new();
        private readonly object _cpuLock = new();
        private const int MaxHistory = 60;

        public StopwatchProfile Profile { get; }

        public bool IsRunning => _stopwatch.IsRunning;
        public bool IsAutoArmed
        {
            get => Profile.IsAutoArmed;
            set => Profile.IsAutoArmed = value;
        }

        public TimeSpan Elapsed => _accumulated + (_stopwatch.IsRunning ? _stopwatch.Elapsed : TimeSpan.Zero);

        public string FormattedTime
        {
            get
            {
                var ts = Elapsed;
                int hours = (int)ts.TotalHours;
                int mins = ts.Minutes;
                int secs = ts.Seconds;
                int tenths = ts.Milliseconds / 100;
                return $"{hours:D2}:{mins:D2}:{secs:D2}.{tenths:D1}";
            }
        }

        public Color AccentColor => ParseColor(Profile.ColorHex, Color.FromArgb(52, 211, 153));

        public bool HasRecordedHistory { get; private set; } = false;

        public event Action? StateChanged;

        public StopwatchItem(StopwatchProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public void RecordCpuSample(float? cpuPercent)
        {
            lock (_cpuLock)
            {
                if (_cpuHistory.Count >= MaxHistory)
                {
                    _cpuHistory.RemoveAt(0);
                }
                _cpuHistory.Add(cpuPercent);
                if (cpuPercent.HasValue && cpuPercent.Value > 0.05f)
                {
                    HasRecordedHistory = true;
                }
            }
        }

        public void ResetCpuHistory()
        {
            lock (_cpuLock)
            {
                _cpuHistory.Clear();
                HasRecordedHistory = false;
            }
        }

        public List<float?> GetCpuHistorySnapshot()
        {
            lock (_cpuLock)
            {
                return new List<float?>(_cpuHistory);
            }
        }

        public void Start()
        {
            if (!_stopwatch.IsRunning)
            {
                if (_accumulated == TimeSpan.Zero)
                {
                    ResetCpuHistory();
                }
                _stopwatch.Start();
                _powerLowDuration = 0f;
                StateChanged?.Invoke();
            }
        }

        public void Stop()
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _accumulated += _stopwatch.Elapsed;
                _stopwatch.Reset();
                // Do not clear CpuHistory here so the recorded curve remains visible
                _powerHighDuration = 0f;
                _powerLowDuration = 0f;
                StateChanged?.Invoke();
            }
        }

        public void Reset()
        {
            _stopwatch.Reset();
            _accumulated = TimeSpan.Zero;
            ResetCpuHistory();
            _powerHighDuration = 0f;
            _powerLowDuration = 0f;
            StateChanged?.Invoke();
        }

        public void ToggleAuto()
        {
            IsAutoArmed = !IsAutoArmed;
            _powerHighDuration = 0f;
            _powerLowDuration = 0f;
            StateChanged?.Invoke();
        }

        public void UpdateAutoTrigger(
            float currentPowerWatts,
            float startThresholdWatts,
            float stopThresholdWatts,
            float hysteresisSec,
            float deltaSec,
            ProcessMonitor? processMonitor)
        {
            if (!IsAutoArmed)
            {
                _powerHighDuration = 0f;
                _powerLowDuration = 0f;
                return;
            }

            bool hasAppTarget = !string.IsNullOrWhiteSpace(Profile.TargetExe);
            bool isAppActiveForStart = true;
            bool isAppActiveForRunning = true;

            if (hasAppTarget && processMonitor != null)
            {
                bool isRunning = processMonitor.IsAppRunning(Profile.TargetExe);
                double cpu = processMonitor.GetAppCpuPercent(Profile.TargetExe);
                isAppActiveForStart = isRunning && cpu >= 0.5;
                isAppActiveForRunning = isRunning && cpu >= 0.2;
            }

            if (!IsRunning)
            {
                // Waiting for power spike + target app activity
                bool canStart = (currentPowerWatts >= startThresholdWatts) && (!hasAppTarget || isAppActiveForStart);

                if (canStart)
                {
                    _powerHighDuration += deltaSec;
                    if (_powerHighDuration >= 1.0f || currentPowerWatts >= startThresholdWatts * 1.15f)
                    {
                        Start();
                        _powerHighDuration = 0f;
                        _powerLowDuration = 0f;
                    }
                }
                else
                {
                    _powerHighDuration = 0f;
                }
            }
            else
            {
                // Currently running: monitor for auto-stop with hysteresis
                bool powerBelowThreshold = currentPowerWatts < stopThresholdWatts;
                bool appStoppedOrIdle = hasAppTarget && !isAppActiveForRunning;

                if (powerBelowThreshold || appStoppedOrIdle)
                {
                    _powerLowDuration += deltaSec;
                    if (_powerLowDuration >= Math.Max(1f, hysteresisSec))
                    {
                        Stop();
                        _powerLowDuration = 0f;
                    }
                }
                else
                {
                    _powerLowDuration = 0f;
                }
            }
        }

        public static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                if (hex.StartsWith("#")) hex = hex[1..];
                if (hex.Length == 6)
                {
                    int r = Convert.ToInt32(hex[0..2], 16);
                    int g = Convert.ToInt32(hex[2..4], 16);
                    int b = Convert.ToInt32(hex[4..6], 16);
                    return Color.FromArgb(r, g, b);
                }
            }
            catch { }
            return fallback;
        }
    }

    public class StopwatchManager
    {
        private readonly List<StopwatchItem> _items = new();
        private readonly StopwatchItem _fallbackItem;

        public IReadOnlyList<StopwatchItem> Items
        {
            get
            {
                lock (_items)
                {
                    return _items.ToArray();
                }
            }
        }

        public StopwatchItem Primary
        {
            get
            {
                lock (_items)
                {
                    return _items.Count > 0 ? _items[0] : _fallbackItem;
                }
            }
        }

        public bool IsRunning => Primary.IsRunning;
        public bool IsAutoArmed
        {
            get => Primary.IsAutoArmed;
            set => Primary.IsAutoArmed = value;
        }

        public TimeSpan Elapsed => Primary.Elapsed;
        public string FormattedTime => Primary.FormattedTime;

        public bool HasAppTriggers
        {
            get
            {
                lock (_items)
                {
                    return _items.Any(i => !string.IsNullOrWhiteSpace(i.Profile.TargetExe));
                }
            }
        }

        public event Action? StateChanged;

        public StopwatchManager(AppSettings? settings = null)
        {
            _fallbackItem = new StopwatchItem(new StopwatchProfile
            {
                Name = "Default",
                ColorHex = "#34D399"
            });
            _fallbackItem.StateChanged += () => StateChanged?.Invoke();

            if (settings != null)
            {
                SyncWithSettings(settings);
            }
        }

        public void SyncWithSettings(AppSettings settings)
        {
            settings.EnsureDefaultStopwatch();

            lock (_items)
            {
                // Detach existing listeners
                foreach (var it in _items)
                {
                    it.StateChanged -= OnItemStateChanged;
                }
                _items.Clear();

                foreach (var profile in settings.Stopwatches)
                {
                    var item = new StopwatchItem(profile);
                    item.StateChanged += OnItemStateChanged;
                    _items.Add(item);
                }
            }

            StateChanged?.Invoke();
        }

        private void OnItemStateChanged()
        {
            StateChanged?.Invoke();
        }

        public void Start() => Primary.Start();
        public void Stop() => Primary.Stop();
        public void Reset() => Primary.Reset();
        public void ToggleAuto() => Primary.ToggleAuto();

        public void UpdateAutoTrigger(
            float currentPowerWatts,
            float startThresholdWatts,
            float stopThresholdWatts,
            float hysteresisSec,
            float deltaSec,
            ProcessMonitor? processMonitor = null)
        {
            List<StopwatchItem> current;
            lock (_items)
            {
                current = _items.ToList();
            }

            foreach (var item in current)
            {
                item.UpdateAutoTrigger(
                    currentPowerWatts,
                    startThresholdWatts,
                    stopThresholdWatts,
                    hysteresisSec,
                    deltaSec,
                    processMonitor
                );
            }
        }
    }
}
