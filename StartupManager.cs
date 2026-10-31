using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public static class StartupManager
    {
        private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string StartupApproved32Path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        private const string AppName = "RyzenQuietPro";

        // Standard Windows 10/11 binary marker for "Enabled" in Task Manager (StartupApproved)
        private static readonly byte[] ApprovedEnabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

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

                using (RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
                {
                    if (runKey != null)
                    {
                        if (enabled)
                        {
                            runKey.SetValue(AppName, $"\"{exePath}\"");
                            Logger.Log($"Startup enabled in Run key: \"{exePath}\"");
                        }
                        else
                        {
                            runKey.DeleteValue(AppName, false);
                            Logger.Log("Startup disabled in Run key.");
                        }
                    }
                }

                // Synchronize and approve state in Task Manager StartupApproved keys
                SetStartupApproved(enabled);
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
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunRegistryPath, false);
                if (runKey == null || runKey.GetValue(AppName) == null)
                {
                    return false;
                }

                // Check Windows Task Manager approval state
                using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, false);
                if (approvedKey?.GetValue(AppName) is byte[] bytes && bytes.Length > 0)
                {
                    // If lowest bit of first byte is set (e.g. 0x01 or 0x03), Windows Task Manager considers it disabled
                    if ((bytes[0] & 1) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SyncExecutablePathIfEnabled()
        {
            try
            {
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true);
                if (runKey != null && runKey.GetValue(AppName) is string currentRegVal)
                {
                    string? currentExe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(currentExe))
                    {
                        currentExe = Application.ExecutablePath;
                    }

                    if (!string.IsNullOrEmpty(currentExe))
                    {
                        string expectedVal = $"\"{currentExe}\"";
                        if (!string.Equals(currentRegVal.Trim('\"'), currentExe, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Log($"Syncing startup executable path from {currentRegVal} to {expectedVal}");
                            runKey.SetValue(AppName, expectedVal);
                        }

                        // Ensure Windows Task Manager state is approved and enabled
                        SetStartupApproved(true);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error syncing startup executable path: {ex.Message}");
            }
        }

        private static void SetStartupApproved(bool enabled)
        {
            try
            {
                using (RegistryKey? approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedPath, true))
                {
                    if (approvedKey != null)
                    {
                        if (enabled)
                        {
                            approvedKey.SetValue(AppName, ApprovedEnabledBytes, RegistryValueKind.Binary);
                            Logger.Log("Startup state approved as Enabled (02) in Task Manager StartupApproved registry.");
                        }
                        else
                        {
                            approvedKey.DeleteValue(AppName, false);
                            Logger.Log("Startup removed from Task Manager StartupApproved registry.");
                        }
                    }
                }

                // Also clean up or sync 32-bit entry if present to avoid conflicting overrides
                try
                {
                    using RegistryKey? approved32Key = Registry.CurrentUser.OpenSubKey(StartupApproved32Path, true);
                    if (approved32Key != null)
                    {
                        if (enabled)
                        {
                            approved32Key.SetValue(AppName, ApprovedEnabledBytes, RegistryValueKind.Binary);
                        }
                        else
                        {
                            approved32Key.DeleteValue(AppName, false);
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating StartupApproved registry: {ex.Message}");
            }
        }
    }
}
