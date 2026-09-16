using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RyzenQuietPro
{
    public class GpuTuningManager
    {
        private readonly HardwareMonitor _hardware;
        private readonly AppSettings _settings;

        public bool IsGpuQuietMode { get; private set; } = true;
        public int CurrentAppliedWatts { get; private set; } = 0;
        public int CurrentAppliedFanCap { get; private set; } = 0;
        public bool IsFanCapActive { get; private set; } = false;
        public bool IsFailSafeActive { get; private set; } = false;

        public int MinSupportedWatts { get; private set; } = 100;
        public int MaxSupportedWatts { get; private set; } = 420;
        public int StockWatts { get; private set; } = 336;

        public event Action? StateChanged;

        public GpuTuningManager(HardwareMonitor hardware, AppSettings settings)
        {
            _hardware = hardware;
            _settings = settings;

            IsGpuQuietMode = _settings.GpuIsQuietMode;
            StockWatts = _settings.GpuStockPowerWatts > 0 ? _settings.GpuStockPowerWatts : 336;

            DetectLimits();
        }

        private void DetectLimits()
        {
            Task.Run(() =>
            {
                try
                {
                    // Query nvidia-smi for power constraints
                    var psi = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "-q -d POWER",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(2000);

                        // Min Power Limit : 100.00 W
                        // Max Power Limit : 450.00 W
                        // Default Power Limit : 420.00 W
                        // Current Power Limit : 336.00 W
                        int min = ParseWatt(output, "Min Power Limit");
                        int max = ParseWatt(output, "Max Power Limit");
                        int def = ParseWatt(output, "Default Power Limit");
                        int cur = ParseWatt(output, "Current Power Limit");

                        if (min > 0) MinSupportedWatts = min;
                        if (max > 0) MaxSupportedWatts = max;
                        if (cur > 0) StockWatts = cur;
                        else if (def > 0) StockWatts = def;

                        Logger.Log($"[GpuTuning] Detected GPU power constraints: Min={MinSupportedWatts}W, Max={MaxSupportedWatts}W, Stock={StockWatts}W");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[GpuTuning] DetectLimits error: {ex.Message}");
                }
            });
        }

        private static int ParseWatt(string text, string key)
        {
            try
            {
                int idx = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int colon = text.IndexOf(':', idx);
                    if (colon > 0)
                    {
                        int end = text.IndexOf('\n', colon);
                        string valStr = end > 0 ? text.Substring(colon + 1, end - colon - 1) : text.Substring(colon + 1);
                        valStr = valStr.Replace("W", "").Trim();
                        if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w))
                        {
                            return (int)Math.Round(w);
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        public void ApplyMode(bool quiet)
        {
            IsGpuQuietMode = quiet;
            _settings.GpuIsQuietMode = quiet;
            _settings.Save();

            if (!_settings.GpuTuningEnabled)
            {
                RestoreStock();
                StateChanged?.Invoke();
                return;
            }

            _hardware.Fans.SetEnabled(true);

            if (quiet)
            {
                int targetWatts = Math.Clamp(_settings.GpuSilentPowerWatts, MinSupportedWatts, MaxSupportedWatts);
                ApplyPowerLimit(targetWatts);

                if (_settings.GpuFanCapEnabled)
                {
                    ApplyFanCap(_settings.GpuFanMaxPercent);
                }
                else
                {
                    ReleaseFanCap();
                }
            }
            else
            {
                // Boost mode -> restore stock TDP & automatic fan curve
                RestoreStock();
            }

            StateChanged?.Invoke();
        }

        public void ApplyPowerLimit(int watts)
        {
            CurrentAppliedWatts = watts;
            Logger.Log($"[GpuTuning] Applying GPU power limit: {watts} W");

            // 1. Send command to FanService via IPC
            _hardware.Fans.SendGpuPowerLimit(watts);

            // 2. Direct nvidia-smi fallback (if process has admin privileges)
            Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = $"-i 0 -pl {watts}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(1500);
                }
                catch { }
            });
        }

        public void RestoreStock()
        {
            int watts = StockWatts > 0 ? StockWatts : 336;
            CurrentAppliedWatts = watts;
            Logger.Log($"[GpuTuning] Restoring stock GPU power: {watts} W");

            _hardware.Fans.ResetGpuPowerLimit(watts);
            ReleaseFanCap();

            Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = $"-i 0 -pl {watts}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(1500);
                }
                catch { }
            });
        }

        public void ApplyFanCap(int percent)
        {
            percent = Math.Clamp(percent, 30, 100);
            CurrentAppliedFanCap = percent;
            IsFanCapActive = true;
            Logger.Log($"[GpuTuning] Applying GPU Fan Cap: {percent} %");

            _hardware.Fans.SendGpuFanCap(percent);
        }

        public void ReleaseFanCap()
        {
            if (IsFanCapActive)
            {
                Logger.Log("[GpuTuning] Releasing GPU Fan Cap to Auto");
                IsFanCapActive = false;
                CurrentAppliedFanCap = 0;
                _hardware.Fans.ResetGpuFan();
            }
        }

        public void CheckFailSafe(float currentTempC)
        {
            int threshold = _settings.GpuFailSafeTempC > 0 ? _settings.GpuFailSafeTempC : 83;

            if (currentTempC >= threshold)
            {
                if (IsFanCapActive && !IsFailSafeActive)
                {
                    IsFailSafeActive = true;
                    Logger.Log($"[GpuTuning] ⚠️ FAIL-SAFE TRIGGERED: GPU Temp {currentTempC:F1}°C >= {threshold}°C! Releasing Fan Cap immediately for safety!");
                    ReleaseFanCap();
                    StateChanged?.Invoke();
                }
            }
            else if (IsFailSafeActive && currentTempC <= threshold - 5)
            {
                // Hysteresis: temperature cooled down below 78°C
                IsFailSafeActive = false;
                Logger.Log($"[GpuTuning] Fail-safe cleared: GPU Temp {currentTempC:F1}°C cooled below threshold.");
                if (IsGpuQuietMode && _settings.GpuFanCapEnabled)
                {
                    ApplyFanCap(_settings.GpuFanMaxPercent);
                }
                StateChanged?.Invoke();
            }
        }
    }
}
