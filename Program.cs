using System;
using System.Threading;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, @"Local\RyzenQuietProMutex", out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                Logger.Log("Another instance of RyzenQuietPro is already running. Exiting.");
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) => {
                Logger.Log($"ThreadException: {e.Exception}");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                Logger.Log($"UnhandledException: {e.ExceptionObject}");
            };

            try
            {
                Logger.Log("Calling Application.Run(new TrayApp())...");
                Application.Run(new TrayApp());
                Logger.Log("Application.Run returned!");
            }
            catch (Exception ex)
            {
                Logger.Log($"Application.Run crash: {ex}");
            }
        }
    }
}
