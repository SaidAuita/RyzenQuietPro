using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuietPro
{
    public class HddScheduleManager : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly HardwareMonitor? _hardware;
        private bool _isCurrentlyWorkHours = false;
        private bool _hasInitialized = false;
        private int _keepAliveCounter = 0;
        private bool _isWakingUp = false;

        public bool IsWakingUp => _isWakingUp;
        public bool IsWorkHours => _isCurrentlyWorkHours;
        public bool IsWorkHoursActive => _isCurrentlyWorkHours && _settings.HddScheduleEnabled;

        public event Action? StatusChanged;

        public HddScheduleManager(AppSettings settings, HardwareMonitor? hardware = null)
        {
            _settings = settings;
            _hardware = hardware;
        }

        public void CheckSchedule()
        {
            if (!_settings.HddScheduleEnabled)
            {
                if (_hasInitialized)
                {
                    _hasInitialized = false;
                }
                return;
            }

            DateTime now = DateTime.Now;
            bool shouldBeWorkHours = IsInWorkHours(now);

            if (!_hasInitialized || shouldBeWorkHours != _isCurrentlyWorkHours)
            {
                _hasInitialized = true;
                _isCurrentlyWorkHours = shouldBeWorkHours;

                if (_isCurrentlyWorkHours)
                {
                    OnWorkHoursEntered();
                }
                else
                {
                    OnOffHoursEntered();
                }

                StatusChanged?.Invoke();
            }
            else if (_isCurrentlyWorkHours && _settings.HddKeepAlivePulse)
            {
                // Pulse every ~90 seconds (assuming CheckSchedule is called once per second)
                _keepAliveCounter++;
                if (_keepAliveCounter >= 90)
                {
                    _keepAliveCounter = 0;
                    _ = Task.Run(SendKeepAlivePulse);
                }
            }
        }

        public bool IsInWorkHours(DateTime dt)
        {
            if (_settings.HddWorkDaysOnly)
            {
                if (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
                {
                    return false;
                }
            }

            TimeSpan current = dt.TimeOfDay;
            TimeSpan start = new TimeSpan(_settings.HddWorkStartHour, _settings.HddWorkStartMinute, 0);
            TimeSpan end = new TimeSpan(_settings.HddWorkEndHour, _settings.HddWorkEndMinute, 0);

            if (start <= end)
            {
                return current >= start && current < end;
            }
            else
            {
                // Overnight schedule support
                return current >= start || current < end;
            }
        }

        public void OnWorkHoursEntered()
        {
            Logger.Log($"[HDD Schedule] Entering Work Hours ({_settings.HddWorkStartHour:D2}:{_settings.HddWorkStartMinute:D2} - {_settings.HddWorkEndHour:D2}:{_settings.HddWorkEndMinute:D2}). Setting disk timeout to 0 (No sleep).");
            PowerPlanManager.SetDiskIdleTimeout(0);
            _ = Task.Run(WakeUpAllHddsAsync);
        }

        public void OnOffHoursEntered()
        {
            int timeout = Math.Max(1, _settings.HddOffHoursTimeoutMinutes);
            Logger.Log($"[HDD Schedule] Entering Off Hours. Setting disk sleep timeout to {timeout} min.");
            PowerPlanManager.SetDiskIdleTimeout(timeout);
        }

        public async Task WakeUpAllHddsAsync()
        {
            if (_isWakingUp) return;
            _isWakingUp = true;
            StatusChanged?.Invoke();

            try
            {
                await Task.Run(() =>
                {
                    Logger.Log("[HDD Schedule] Waking up all fixed disks in background...");
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            try
                            {
                                // Accessing volume info forces physical spindle spin-up
                                _ = drive.TotalFreeSpace;
                                var root = new DirectoryInfo(drive.RootDirectory.FullName);
                                _ = root.GetDirectories();
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"[HDD Schedule] Drive wake probe warning ({drive.Name}): {ex.Message}");
                            }
                        }
                    }
                    Logger.Log("[HDD Schedule] Disk wake-up cycle complete.");
                });
            }
            finally
            {
                _isWakingUp = false;
                StatusChanged?.Invoke();
            }
        }

        private void SendKeepAlivePulse()
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        try
                        {
                            // Lightweight volume query to reset internal APM idle counter
                            _ = drive.TotalFreeSpace;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public string GetStatusString()
        {
            if (!_settings.HddScheduleEnabled)
            {
                return Loc.Get("HddStatus_Standby").Replace("{0}", "20");
            }

            if (_isWakingUp)
            {
                return Loc.Get("HddWaking");
            }

            if (_isCurrentlyWorkHours)
            {
                return $"{Loc.Get("HddStatus_Awake")} (до {_settings.HddWorkEndHour:D2}:{_settings.HddWorkEndMinute:D2})";
            }
            else
            {
                return Loc.Get("HddStatus_Standby").Replace("{0}", _settings.HddOffHoursTimeoutMinutes.ToString());
            }
        }

        public void ApplyImmediate()
        {
            _hasInitialized = false;
            CheckSchedule();
        }

        public void Dispose()
        {
        }
    }
}
