using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuiet.Mac
{
    public class MacTerminalDashboard
    {
        private readonly MacCpuMonitor _cpu;
        private readonly MacRamMonitor _ram;
        private readonly MacGpuMonitor _gpu;
        private readonly MacDiskMonitor _disk;
        private readonly MacFanMonitor _fans;
        private readonly MacPowerManager _power;

        private bool _fanIconsMode = false;
        private bool _isRunning = true;

        public MacTerminalDashboard(
            MacCpuMonitor cpu,
            MacRamMonitor ram,
            MacGpuMonitor gpu,
            MacDiskMonitor disk,
            MacFanMonitor fans,
            MacPowerManager power)
        {
            _cpu = cpu;
            _ram = ram;
            _gpu = gpu;
            _disk = disk;
            _fans = fans;
            _power = power;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.CursorVisible = false; } catch { }

            // Start keyboard input loop
            _ = Task.Run(() => ReadKeyboardLoop(ct), ct);

            try
            {
                while (!ct.IsCancellationRequested && _isRunning)
                {
                    _cpu.Sample();
                    _ram.Sample();
                    _gpu.Sample();
                    _disk.Sample();
                    _fans.Sample();

                    RenderScreen();
                    await Task.Delay(1000, ct);
                }
            }
            finally
            {
                try { Console.CursorVisible = true; } catch { }
                try { Console.ResetColor(); } catch { }
            }
        }

        private void ReadKeyboardLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        switch (key.Key)
                        {
                            case ConsoleKey.Q:
                            case ConsoleKey.S:
                                _power.SetMode(MacPowerManager.Mode.Quiet);
                                break;
                            case ConsoleKey.B:
                                _power.SetMode(MacPowerManager.Mode.Boost);
                                break;
                            case ConsoleKey.F:
                                _fanIconsMode = !_fanIconsMode;
                                break;
                            case ConsoleKey.D:
                                _fans.DemoMode = !_fans.DemoMode;
                                break;
                            case ConsoleKey.Escape:
                            case ConsoleKey.X:
                                _isRunning = false;
                                break;
                        }
                    }
                }
                catch
                {
                    // Non-interactive or redirected terminal (e.g. background run or pipe)
                    Thread.Sleep(500);
                    continue;
                }
                Thread.Sleep(50);
            }
        }

        private void RenderScreen()
        {
            var sb = new StringBuilder();
            sb.Append("\x1b[H"); // Move cursor home

            int termW = 80;
            try
            {
                termW = Math.Max(70, Math.Min(100, Console.WindowWidth));
            }
            catch
            {
                termW = 80;
            }

            // 1. Header & Branding
            sb.AppendLine("\x1b[38;2;56;189;248m╔" + new string('═', termW - 2) + "╗\x1b[0m");
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[1m ⚡ RyzenQuiet PRO (v3.0 macOS Edition)\x1b[0m" +
                          $"\x1b[38;2;148;163;184m [ThinkPad x230 / Hackintosh / VM]\x1b[0m" +
                          new string(' ', Math.Max(0, termW - 74)) +
                          "\x1b[38;2;56;189;248m║\x1b[0m");
            sb.AppendLine("\x1b[38;2;56;189;248m╠" + new string('═', termW - 2) + "╣\x1b[0m");

            // 2. Power Mode Banner
            string quietBadge = _power.IsQuietMode
                ? "\x1b[48;2;22;101;52m\x1b[1;37m [🌿 SILENT 99% (Active)] \x1b[0m"
                : "\x1b[38;2;100;116;139m [🌿 Silent 99%] \x1b[0m";
            string boostBadge = !_power.IsQuietMode
                ? "\x1b[48;2;194;65;12m\x1b[1;37m [⚡ BOOST 100% (Active)] \x1b[0m"
                : "\x1b[38;2;100;116;139m [⚡ Boost 100%] \x1b[0m";

            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m  Mode: {quietBadge}  {boostBadge}   \x1b[90m(Keys: [S]ilent, [B]oost, [F]an mode, [D]emo, [X]exit)\x1b[0m");
            sb.AppendLine("\x1b[38;2;56;189;248m╟" + new string('─', termW - 2) + "╢\x1b[0m");

            // 3. CPU Section
            string cpuSub = $"\x1b[1m{_cpu.CpuName}\x1b[0m | {_cpu.CurrentGhz:F2} GHz | {_cpu.LogicalCores} потоков ({_cpu.PhysicalCores}C/{_cpu.LogicalCores}T)";
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m \x1b[1;32mCPU:\x1b[0m {_cpu.TotalPercent,5:F1}%   \x1b[38;2;148;163;184m{cpuSub}\x1b[0m");
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m  {RenderSparkline(_cpu.History, 48, "\x1b[38;2;34;197;94m")}");

            // CPU Core Heatmap
            sb.Append("\x1b[38;2;56;189;248m║\x1b[0m  Cores: ");
            for (int c = 0; c < _cpu.CoreLoads.Length; c++)
            {
                float load = _cpu.CoreLoads[c];
                string col = load > 75 ? "\x1b[38;2;239;68;68m" : load > 40 ? "\x1b[38;2;245;158;11m" : "\x1b[38;2;34;197;94m";
                sb.Append($"{col}[T{c + 1}:{load,3:F0}%]\x1b[0m ");
            }
            sb.AppendLine();

            // Top Processes
            if (_cpu.TopProcesses.Count > 0)
            {
                sb.Append("\x1b[38;2;56;189;248m║\x1b[0m  \x1b[90mTop: ");
                foreach (var p in _cpu.TopProcesses)
                {
                    string pName = p.Name.Length > 10 ? p.Name.Substring(0, 10) : p.Name;
                    sb.Append($"{pName} ({p.CpuPercent:F1}%)  ");
                }
                sb.AppendLine("\x1b[0m");
            }
            sb.AppendLine("\x1b[38;2;56;189;248m╟" + new string('─', termW - 2) + "╢\x1b[0m");

            // 4. RAM Section
            string ramSub = $"\x1b[1m{_ram.MemorySpecs}\x1b[0m | {_ram.UsedGb:F1} / {_ram.TotalGb:F1} GB ({_ram.UsagePercent:F1}%)";
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m \x1b[1;36mRAM:\x1b[0m {_ram.UsagePercent,5:F1}%   \x1b[38;2;148;163;184m{ramSub}\x1b[0m");
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m  {RenderSparkline(_ram.History, 48, "\x1b[38;2;6;182;212m")}");
            sb.AppendLine("\x1b[38;2;56;189;248m╟" + new string('─', termW - 2) + "╢\x1b[0m");

            // 5. GPU & VRAM Section
            string gpuSub = $"\x1b[1m{_gpu.GpuName}\x1b[0m | {_gpu.VramType} {_gpu.VramUsedGb:F1} / {_gpu.VramTotalGb:F1} GB";
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m \x1b[1;35mGPU / VRAM:\x1b[0m {_gpu.GpuLoadPercent,5:F1}%   \x1b[38;2;148;163;184m{gpuSub}\x1b[0m");
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m  {RenderSparkline(_gpu.History, 48, "\x1b[38;2;168;85;247m")}");
            sb.AppendLine("\x1b[38;2;56;189;248m╟" + new string('─', termW - 2) + "╢\x1b[0m");

            // 6. DISK Section
            string diskStatus = _disk.IsIdle ? "\x1b[90m[Idle]\x1b[0m" : "\x1b[32m[Active]\x1b[0m";
            string diskSub = $"\x1b[1m{_disk.ActiveDiskName}\x1b[0m | R:{_disk.ReadSpeedMBps:F1} W:{_disk.WriteSpeedMBps:F1} MB/s {diskStatus}";
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m \x1b[1;33mDISK:\x1b[0m {_disk.ActivityPercent,5:F1}%  \x1b[38;2;148;163;184m{diskSub}\x1b[0m");
            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m  {RenderSparkline(_disk.History, 48, "\x1b[38;2;234;179;8m")}");
            sb.AppendLine("\x1b[38;2;56;189;248m╟" + new string('─', termW - 2) + "╢\x1b[0m");

            // 7. FANS Section (Switchable: Graph vs Icons)
            string fanModeStr = _fanIconsMode ? "🌀 ИКОНКИ (Impeller)" : "📈 ГРАФИК (Curves)";
            string demoStr = _fans.DemoMode ? "\x1b[33m[👁️ Demo ON]\x1b[0m" : "\x1b[90m[SMC]\x1b[0m";
            var fansList = _fans.Fans;
            int primaryRpm = fansList.Count > 0 ? fansList[0].CurrentRpm : 0;

            sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m \x1b[1;34mFANS:\x1b[0m {primaryRpm,5} RPM   \x1b[38;2;148;163;184mРежим: {fanModeStr} {demoStr}\x1b[0m");

            if (_fanIconsMode)
            {
                // Fan Impeller Cards with blade counts scaling by speed
                sb.Append("\x1b[38;2;56;189;248m║\x1b[0m  ");
                foreach (var f in fansList)
                {
                    int blades = f.CurrentRpm == 0 ? 3 : f.CurrentRpm < 1200 ? 4 : f.CurrentRpm < 2200 ? 7 : 11;
                    string tierName = f.CurrentRpm == 0 ? "OFF" : f.CurrentRpm < 1200 ? "QUIET" : f.CurrentRpm < 2200 ? "MED" : "TURBO";
                    string col = f.CurrentRpm == 0 ? "\x1b[90m" : f.CurrentRpm < 1200 ? "\x1b[38;2;56;189;248m" : f.CurrentRpm < 2200 ? "\x1b[38;2;249;115;22m" : "\x1b[38;2;239;68;68m";
                    string icon = f.CurrentRpm == 0 ? "⊘" : f.CurrentRpm < 1200 ? "❄" : f.CurrentRpm < 2200 ? "🌀" : "⚡";
                    sb.Append($"{col}[{icon} {f.Name}: {f.CurrentRpm} RPM ({blades}b/{tierName})]\x1b[0m  ");
                }
                sb.AppendLine();
            }
            else
            {
                if (fansList.Count > 0)
                {
                    sb.AppendLine($"\x1b[38;2;56;189;248m║\x1b[0m  {RenderSparkline(fansList[0].History, 48, "\x1b[38;2;14;165;233m")}");
                }
            }

            sb.AppendLine("\x1b[38;2;56;189;248m╚" + new string('═', termW - 2) + "╝\x1b[0m");
            Console.Write(sb.ToString());
        }

        private static string RenderSparkline(IReadOnlyList<float> history, int width, string colorEsc)
        {
            if (history == null || history.Count == 0) return "";
            char[] glyphs = new[] { ' ', ' ', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

            var sb = new StringBuilder();
            sb.Append(colorEsc);

            int start = Math.Max(0, history.Count - width);
            for (int i = start; i < history.Count; i++)
            {
                float val = Math.Clamp(history[i], 0f, 100f);
                int idx = (int)Math.Round((val / 100f) * (glyphs.Length - 1));
                sb.Append(glyphs[Math.Clamp(idx, 0, glyphs.Length - 1)]);
            }

            sb.Append("\x1b[0m");
            return sb.ToString();
        }
    }
}
