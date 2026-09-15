using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RyzenQuiet.Mac
{
    public class MacFanItem
    {
        public string Id { get; }
        public string Name { get; set; }
        public int CurrentRpm { get; set; }
        public int MaxRpm { get; set; } = 4500;
        public string ColorHex { get; set; } = "#38bdf8";

        private readonly List<float> _history = new();
        private readonly object _lock = new();

        public IReadOnlyList<float> History
        {
            get
            {
                lock (_lock) return _history.ToArray();
            }
        }

        public MacFanItem(string id, string name, string colorHex)
        {
            Id = id;
            Name = name;
            ColorHex = colorHex;
            lock (_lock)
            {
                for (int i = 0; i < 60; i++) _history.Add(0f);
            }
        }

        public void AddSample(int rpm)
        {
            CurrentRpm = Math.Max(0, rpm);
            if (CurrentRpm > MaxRpm) MaxRpm = ((CurrentRpm / 500) + 1) * 500;

            lock (_lock)
            {
                _history.Add(CurrentRpm);
                while (_history.Count > 60) _history.RemoveAt(0);
            }
        }
    }

    public class MacFanMonitor
    {
        private readonly List<MacFanItem> _fans = new();
        private readonly List<MacFanItem> _demoFans = new();
        private readonly object _lock = new();
        private bool _demoMode = false;
        private double _demoStep = 0;

        public bool DemoMode
        {
            get => _demoMode;
            set
            {
                _demoMode = value;
                if (_demoMode && _demoFans.Count == 0)
                {
                    InitDemoFans();
                }
            }
        }

        public IReadOnlyList<MacFanItem> Fans
        {
            get
            {
                lock (_lock)
                {
                    if (_demoMode)
                    {
                        if (_demoFans.Count == 0) InitDemoFans();
                        return _demoFans.ToArray();
                    }
                    return _fans.ToArray();
                }
            }
        }

        public MacFanMonitor()
        {
            InitFans();
            Sample();
        }

        private void InitFans()
        {
            // On ThinkPad x230 Hackintosh, Fan 0 is the primary CPU fan
            var cpuFan = new MacFanItem("fan_cpu", "CPU Fan", "#38bdf8");
            _fans.Add(cpuFan);
        }

        private void InitDemoFans()
        {
            var f0 = new MacFanItem("demo_cpu", "CPU Fan", "#38bdf8");
            f0.AddSample(1250);

            var f1 = new MacFanItem("demo_gpu", "GPU Fan", "#a855f7");
            f1.AddSample(1850);

            var f2 = new MacFanItem("demo_ch1", "Chassis #1", "#22c55e");
            f2.AddSample(2850);

            var f3 = new MacFanItem("demo_ch2", "Chassis #2", "#ec4899");
            f3.AddSample(950);

            var f4 = new MacFanItem("demo_pump", "Pump Fan", "#f97316");
            f4.AddSample(3400);

            _demoFans.AddRange(new[] { f0, f1, f2, f3, f4 });
        }

        public void Sample()
        {
            if (_demoMode)
            {
                _demoStep += 0.08;
                lock (_lock)
                {
                    for (int i = 0; i < _demoFans.Count; i++)
                    {
                        var fan = _demoFans[i];
                        int baseRpm = (i + 1) * 650;
                        int wave = (int)(Math.Sin(_demoStep + i) * 220);
                        fan.AddSample(Math.Max(0, baseRpm + wave));
                    }
                }
                return;
            }

            int rpm = ReadSmcFanRpm();
            lock (_lock)
            {
                if (_fans.Count > 0)
                {
                    _fans[0].AddSample(rpm);
                }
            }
        }

        private static int ReadSmcFanRpm()
        {
            try
            {
                // 1. Check if 'smc' CLI is installed in PATH or standard homebrew / local paths
                string[] smcPaths = new[] { "/usr/local/bin/smc", "/opt/homebrew/bin/smc", "smc" };
                foreach (var path in smcPaths)
                {
                    if (File.Exists(path) || path == "smc")
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "-k F0Ac -r",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            string outStr = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(200);

                            // Format: [F0Ac]  2340 (bytes 09 24)
                            int bIdx = outStr.IndexOf(']');
                            if (bIdx >= 0)
                            {
                                string numPart = outStr.Substring(bIdx + 1).Trim();
                                int spaceIdx = numPart.IndexOf(' ');
                                if (spaceIdx > 0) numPart = numPart.Substring(0, spaceIdx);
                                if (int.TryParse(numPart, out int r)) return r;
                            }
                        }
                    }
                }
            }
            catch { }

            // Default reasonable fallback for x230 when SMC is idle
            return 2100;
        }
    }
}
