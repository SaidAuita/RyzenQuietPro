using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public static class StartupManager
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "RyzenQuietPro";

        public static void SetStartup(bool enabled)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Application.ExecutablePath;
                }

                if (string.IsNullOrEmpty(exePath))
                {
                    Logger.Log("Failed to determine executable path for startup.");
                    return;
                }

                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key != null)
                {
                    if (enabled)
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        Logger.Log($"Startup enabled: \"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                        Logger.Log("Startup disabled.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error managing startup: {ex.Message}");
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
                if (key != null)
                {
                    object? val = key.GetValue(AppName);
                    return val != null;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
