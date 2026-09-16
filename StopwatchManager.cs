using System;
using System.Diagnostics;

namespace RyzenQuietPro
{
    public class StopwatchManager
    {
        private readonly Stopwatch _stopwatch = new();
        private TimeSpan _accumulated = TimeSpan.Zero;
        private float _powerHighDuration = 0f;
        private float _powerLowDuration = 0f;

        public bool IsRunning => _stopwatch.IsRunning;
        public bool IsAutoArmed { get; set; } = false;

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

        public event Action? StateChanged;

        public void Start()
        {
            if (!_stopwatch.IsRunning)
            {
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
                _powerHighDuration = 0f;
                _powerLowDuration = 0f;
                StateChanged?.Invoke();
            }
        }

        public void Reset()
        {
            _stopwatch.Reset();
            _accumulated = TimeSpan.Zero;
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

        public void UpdateAutoTrigger(float currentPowerWatts, float startThresholdWatts, float stopThresholdWatts, float hysteresisSec, float deltaSec)
        {
            if (!IsAutoArmed)
            {
                _powerHighDuration = 0f;
                _powerLowDuration = 0f;
                return;
            }

            if (!IsRunning)
            {
                // Waiting for power spike to auto-start
                if (currentPowerWatts >= startThresholdWatts)
                {
                    _powerHighDuration += deltaSec;
                    // Trigger start immediately or after 1 second of confirmed load
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
                if (currentPowerWatts < stopThresholdWatts)
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
                    // Power went back up during hysteresis window (e.g. game loading screen finished)
                    _powerLowDuration = 0f;
                }
            }
        }
    }
}
