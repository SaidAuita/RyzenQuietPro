using System;
using System.IO;
using System.Text.Json;

namespace RyzenQuietPro
{
    public class AppSettings
    {
        public bool LiveTrayIcon { get; set; } = true;
        public int TrayMetric { get; set; } = 0; // 0 = CPU, 1 = GPU, 2 = RAM
        public bool AutoQuietOnIdle { get; set; } = false;
        public int IdleMinutes { get; set; } = 5;
        public bool ShowTopProcesses { get; set; } = false;
        public bool ShowAllGpus { get; set; } = false;

        // GPU Acoustic & Power Tuning
        public bool GpuTuningEnabled { get; set; } = true;
        public bool SeparateCpuGpuControl { get; set; } = false;
        public bool GpuIsQuietMode { get; set; } = true;
        public int GpuSilentPowerWatts { get; set; } = 240;
        public int GpuStockPowerWatts { get; set; } = 336;
        public bool GpuFanCapEnabled { get; set; } = false;
        public int GpuFanMaxPercent { get; set; } = 45; // 30% to 100%
        public int GpuTargetTempC { get; set; } = 75;
        public int GpuFailSafeTempC { get; set; } = 83;

        // Stopwatch Module Settings
        public bool ShowStopwatch { get; set; } = true;
        public bool StopwatchAutoStartEnabled { get; set; } = false;
        public int StopwatchTriggerSource { get; set; } = 0; // 0 = Max(CPU, GPU), 1 = CPU, 2 = GPU, 3 = Total (CPU + GPU)
        public int StopwatchAutoStartWatts { get; set; } = 50;
        public int StopwatchAutoStopWatts { get; set; } = 30;
        public int StopwatchHysteresisSeconds { get; set; } = 3;
        public System.Collections.Generic.List<StopwatchProfile> Stopwatches { get; set; } = new();


        // Visible Modules & Graphs
        public bool ShowCpuGraph { get; set; } = true;
        public int CpuGraphScale { get; set; } = 1; // 1 = 1x (100%), 2 = 2x (50%), 4 = 4x (25%), 6 = 6x (16.7%), 8 = 8x (12.5%), 0 = Auto
        public bool ShowCpuCores { get; set; } = true;
        public bool ShowRamGraph { get; set; } = true;
        public bool ShowGpuGraph { get; set; } = true;
        public bool ShowGpuTempLine { get; set; } = true;
        public bool ShowGpuFanSpeed { get; set; } = true;
        public bool ShowVramGraph { get; set; } = true;
        public bool ShowDiskGraph { get; set; } = true;
        public bool EnhancedDiskMode { get; set; } = false;
        public bool DiskLegendExpanded { get; set; } = false;
        public System.Collections.Generic.List<string> HiddenDiskIds { get; set; } = new();

        public bool IsDiskVisible(string diskId, string instanceName)
        {
            if (HiddenDiskIds == null || HiddenDiskIds.Count == 0) return true;
            return !HiddenDiskIds.Contains(diskId) && !HiddenDiskIds.Contains(instanceName);
        }

        public void SetDiskVisibility(string diskId, bool visible)
        {
            HiddenDiskIds ??= new();
            if (visible)
            {
                HiddenDiskIds.Remove(diskId);
            }
            else
            {
                if (!HiddenDiskIds.Contains(diskId))
                    HiddenDiskIds.Add(diskId);
            }
        }
        public bool ShowFanGraph { get; set; } = false;
        public bool EnableFanAddon { get; set; } = false;
        public int FanVisualMode { get; set; } = 0; // 0 = Graph (default), 1 = 1 Row (Icons), 2 = Grid (Multi-row)
        public int FanScalePercent { get; set; } = 100; // 100% to 200%
        public bool FanLargeIcons
        {
            get => FanScalePercent >= 150;
            set
            {
                if (value && FanScalePercent < 150) FanScalePercent = 200;
                else if (!value && FanScalePercent >= 150) FanScalePercent = 100;
            }
        }
        [System.Text.Json.Serialization.JsonIgnore]
        public float FanScale => Math.Clamp(FanScalePercent, 80, 250) / 100f;
        public int LastKnownFanCount { get; set; } = 0;
        public bool EnableFanDemo { get; set; } = false;
        public System.Collections.Generic.List<string> HiddenFanIds { get; set; } = new();

        public bool IsFanVisible(string fanId, string fanName)
        {
            if (HiddenFanIds == null || HiddenFanIds.Count == 0) return true;
            return !HiddenFanIds.Contains(fanId) && !HiddenFanIds.Contains(fanName);
        }

        public void SetFanVisibility(string fanId, bool visible)
        {
            HiddenFanIds ??= new();
            if (visible)
            {
                HiddenFanIds.Remove(fanId);
            }
            else
            {
                if (!HiddenFanIds.Contains(fanId))
                    HiddenFanIds.Add(fanId);
            }
        }

        public string Language { get; set; } = "auto";

        // Detached Floating HUD settings
        public bool IsDetached { get; set; } = false;
        public int WindowX { get; set; } = -1;
        public int WindowY { get; set; } = -1;
        public int WindowWidth { get; set; } = 420;
        public int WindowHeight { get; set; } = 530;
        public bool AlwaysOnTop { get; set; } = true;
        public double WidgetOpacity { get; set; } = 0.96;
        public int SettingsWidth { get; set; } = 500;
        public int SettingsHeight { get; set; } = 0;

        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RyzenQuietPro"
        );
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static AppSettings Load()
        {
            AppSettings settings;
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    settings = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load settings: {ex.Message}");
                settings = new AppSettings();
            }

            settings.EnsureDefaultStopwatch();
            return settings;
        }

        public void EnsureDefaultStopwatch()
        {
            Stopwatches ??= new();
            if (Stopwatches.Count == 0)
            {
                Stopwatches.Add(new StopwatchProfile
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "",
                    TargetExe = "",
                    TargetFriendlyName = "",
                    ColorHex = "#34D399", // Emerald
                    IsAutoArmed = StopwatchAutoStartEnabled,
                    ShowOnCpuGraph = true,
                    GraphScale = 0
                });
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save settings: {ex.Message}");
            }
        }
    }

    public class StopwatchProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string TargetExe { get; set; } = ""; // Empty string = Any program / Global power
        public string TargetFriendlyName { get; set; } = "";
        public string ColorHex { get; set; } = "#34D399";
        public bool IsAutoArmed { get; set; } = false;
        public bool ShowOnCpuGraph { get; set; } = true;
        public int GraphScale { get; set; } = 0; // 0 = Auto, 1 = 1x, 2 = 2x, 4 = 4x, 8 = 8x, 16 = 16x, -1 = Off
    }
}
