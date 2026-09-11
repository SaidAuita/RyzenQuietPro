using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RyzenQuietPro
{
    public class PowerPlanManager
    {
        private const string SubProcessor = "SUB_PROCESSOR";
        private const string ProcThrottleMax = "PROCTHROTTLEMAX";
        private const string ProcThrottleMin = "PROCTHROTTLEMIN";
        private const string PerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";

        public enum Mode
        {
            Quiet = 99,
            Normal = 100,
            Unknown = 0
        }

        public static bool SetMode(Mode mode)
        {
            if (mode != Mode.Quiet && mode != Mode.Normal)
                return false;

            int targetMax = (int)mode;
            int targetBoost = (mode == Mode.Quiet) ? 0 : 2; // 0 = Disabled, 2 = Aggressive (Precision Boost on)

            try
            {
                // 1. Ensure Min <= Max (critical on High Performance plans where Min is 100% by default)
                RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {ProcThrottleMin} 5");
                RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {ProcThrottleMin} 5");

                // 2. Set Max Throttle (99% or 100%)
                var acResult = RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {ProcThrottleMax} {targetMax}");
                if (acResult.ExitCode != 0)
                {
                    Logger.Log($"Failed to set AC value to {targetMax}: {acResult.StdErr}");
                    return false;
                }

                var dcResult = RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {ProcThrottleMax} {targetMax}");
                if (dcResult.ExitCode != 0)
                {
                    Logger.Log($"Failed to set DC value to {targetMax}: {dcResult.StdErr}");
                    return false;
                }

                // 3. Set Processor Performance Boost Mode (0 = Disabled, 2 = Aggressive)
                RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {PerfBoostMode} {targetBoost}");
                RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {PerfBoostMode} {targetBoost}");

                // 4. Activate current scheme so settings take effect immediately
                var actResult = RunPowerCfg("/setactive SCHEME_CURRENT");
                if (actResult.ExitCode != 0)
                {
                    Logger.Log($"Failed to activate current power scheme: {actResult.StdErr}");
                    return false;
                }

                // 5. Verify that the change was applied
                int currentAc = GetCurrentThrottleMax();
                if (currentAc != targetMax)
                {
                    Logger.Log($"Verification failed: expected {targetMax}%, but powercfg reported {currentAc}%");
                    return false;
                }

                Logger.Log($"Successfully applied mode {mode} (Max: {targetMax}%, BoostMode: {targetBoost}). Current AC state: {currentAc}%");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception setting power mode {mode}: {ex.Message}");
                return false;
            }
        }

        public static int GetCurrentThrottleMax()
        {
            try
            {
                var result = RunPowerCfg($"/query SCHEME_CURRENT {SubProcessor} {ProcThrottleMax}");
                if (result.ExitCode != 0)
                {
                    Logger.Log($"Failed to query power scheme: {result.StdErr}");
                    return -1;
                }

                var matches = Regex.Matches(result.StdOut, @"0x([0-9a-fA-F]+)");
                if (matches.Count >= 2)
                {
                    string hexValue = matches[matches.Count - 2].Groups[1].Value;
                    return Convert.ToInt32(hexValue, 16);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reading current throttle max: {ex.Message}");
            }
            return -1;
        }

        public static Mode GetCurrentMode()
        {
            int throttle = GetCurrentThrottleMax();
            if (throttle == (int)Mode.Quiet) return Mode.Quiet;
            if (throttle == (int)Mode.Normal) return Mode.Normal;
            return Mode.Unknown;
        }

        public struct ProcessResult
        {
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        private static ProcessResult RunPowerCfg(string args)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdOut,
                StdErr = stdErr
            };
        }
    }
}
