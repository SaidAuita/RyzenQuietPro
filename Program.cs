using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    static class Program
    {
        private static Mutex? _mutex;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int HWND_BROADCAST = 0xffff;

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
                Logger.Log("Another instance of RyzenQuietPro is already running. Signaling existing instance to show dashboard.");
                try
                {
                    uint msg = RegisterWindowMessage("RyzenQuietPro_ShowDashboard");
                    PostMessage((IntPtr)HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
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
