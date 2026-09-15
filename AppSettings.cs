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

        // Visible Modules & Graphs
        public bool ShowCpuGraph { get; set; } = true;
        public bool ShowCpuCores { get; set; } = true;
        public bool ShowRamGraph { get; set; } = true;
        public bool ShowGpuGraph { get; set; } = true;
        public bool ShowGpuTempLine { get; set; } = true;
        public bool ShowGpuFanSpeed { get; set; } = true;
        public bool ShowVramGraph { get; set; } = true;
        public bool ShowDiskGraph { get; set; } = true;
        public bool ShowFanGraph { get; set; } = false;
        public bool EnableFanAddon { get; set; } = false;
        public int FanVisualMode { get; set; } = 0; // 0 = Graph (default), 1 = 1 Row (Icons), 2 = Grid (Multi-row)
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

        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RyzenQuietPro"
        );
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load settings: {ex.Message}");
            }
            return new AppSettings();
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
}
