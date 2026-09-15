using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuiet.Mac
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.Title = "⚡ RyzenQuiet PRO (macOS Edition)";

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var cpu = new MacCpuMonitor();
            var ram = new MacRamMonitor();
            var gpu = new MacGpuMonitor();
            var disk = new MacDiskMonitor();
            var fans = new MacFanMonitor();
            var power = new MacPowerManager();

            // Check arguments
            bool openBrowser = false;
            bool demo = false;

            foreach (var a in args)
            {
                if (a.Equals("--demo", StringComparison.OrdinalIgnoreCase) || a.Equals("-d", StringComparison.OrdinalIgnoreCase))
                {
                    demo = true;
                }
                else if (a.Equals("--web", StringComparison.OrdinalIgnoreCase) || a.Equals("-w", StringComparison.OrdinalIgnoreCase))
                {
                    openBrowser = true;
                }
                else if (a.Equals("--boost", StringComparison.OrdinalIgnoreCase) || a.Equals("-b", StringComparison.OrdinalIgnoreCase))
                {
                    power.SetMode(MacPowerManager.Mode.Boost);
                }
            }

            if (demo)
            {
                fans.DemoMode = true;
            }

            // Start Web Dashboard Bridge (Safari / Localhost:5050)
            var web = new MacWebWidget(cpu, ram, gpu, disk, fans, power);
            web.Start(cts.Token);

            if (openBrowser)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/usr/bin/open",
                        Arguments = $"http://localhost:{web.Port}",
                        UseShellExecute = false
                    });
                }
                catch { }
            }

            // Run interactive Terminal Live HUD
            var term = new MacTerminalDashboard(cpu, ram, gpu, disk, fans, power);
            await term.RunAsync(cts.Token);

            return 0;
        }
    }
}
