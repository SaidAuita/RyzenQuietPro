using System;
using System.IO;

namespace RyzenQuietPro
{
    public static class Logger
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RyzenQuietPro"
        );
        private static readonly string LogFile = Path.Combine(LogFolder, "debug.log");
        private static readonly object Lock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(LogFolder);
                    File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Silently ignore logging failures
            }
        }
    }
}
