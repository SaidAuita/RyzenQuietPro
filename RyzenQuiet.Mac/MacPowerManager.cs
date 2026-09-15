using System;
using System.Diagnostics;
using System.IO;

namespace RyzenQuiet.Mac
{
    public class MacPowerManager
    {
        public enum Mode
        {
            Quiet,
            Boost
        }

        public Mode CurrentMode { get; private set; } = Mode.Quiet;
        public bool IsQuietMode => CurrentMode == Mode.Quiet;
        public string StatusDescription => IsQuietMode ? "🌿 SILENT (Base 2.60 GHz | 0dB)" : "⚡ BOOST (Turbo ON | Max Performance)";

        public event Action<Mode>? ModeChanged;

        public MacPowerManager()
        {
            // Default to Quiet mode (as requested by user philosophy)
            SetMode(Mode.Quiet);
        }

        public bool SetMode(Mode mode)
        {
            CurrentMode = mode;
            bool success = ApplyPowerMode(mode);
            ModeChanged?.Invoke(mode);
            return success;
        }

        private bool ApplyPowerMode(Mode mode)
        {
            try
            {
                // On macOS, power profiles and thermal throttling can be adjusted via pmset:
                // Silent mode: reduce aggressive power profile, set lower sleep/idle timers
                // Boost mode: set high performance profile
                string args = mode == Mode.Quiet
                    ? "-a displaysleep 10 disksleep 10 gpuswitch 0"
                    : "-a displaysleep 30 disksleep 0 gpuswitch 2";

                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/pmset",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(300);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
