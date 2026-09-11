using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RyzenQuietPro
{
    public static class AppIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Bitmap QuietBitmap { get; private set; } = null!;
        public static Bitmap NormalBitmap { get; private set; } = null!;
        public static Icon QuietIcon { get; private set; } = null!;
        public static Icon NormalIcon { get; private set; } = null!;

        private static bool _initialized;

        static AppIcons()
        {
            Initialize();
        }

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                QuietBitmap = LoadBitmapInternal(@"ico\32\quiet_32.png", "quiet_32.png", Color.FromArgb(46, 204, 113));
                NormalBitmap = LoadBitmapInternal(@"ico\32\normal_32.png", "normal_32.png", Color.FromArgb(243, 156, 18));

                QuietIcon = LoadIconInternal(@"Resources\quiet.ico", "quiet.ico") ?? IconFromBitmap(QuietBitmap);
                NormalIcon = LoadIconInternal(@"Resources\normal.ico", "normal.ico") ?? IconFromBitmap(NormalBitmap);
            }
            catch (Exception ex)
            {
                Logger.Log($"AppIcons.Initialize error: {ex.Message}");
            }

            _initialized = true;
        }

        public static Bitmap LoadBitmapInternal(string diskPath, string resName, Color fallbackColor)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string fullPath = Path.Combine(baseDir, diskPath);
                if (!File.Exists(fullPath)) fullPath = Path.GetFullPath(diskPath);

                if (File.Exists(fullPath)) return new Bitmap(fullPath);

                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(resName, StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream != null) return new Bitmap(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"LoadBitmapInternal error ({resName}): {ex.Message}");
            }

            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp)) g.Clear(fallbackColor);
            return bmp;
        }

        public static Icon? LoadIconInternal(string diskPath, string resName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string fullPath = Path.Combine(baseDir, diskPath);
                if (!File.Exists(fullPath)) fullPath = Path.GetFullPath(diskPath);

                if (File.Exists(fullPath)) return new Icon(fullPath);

                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(resName, StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream != null) return new Icon(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"LoadIconInternal error ({resName}): {ex.Message}");
            }

            return null;
        }

        public static Icon IconFromBitmap(Bitmap bmp)
        {
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }
}
