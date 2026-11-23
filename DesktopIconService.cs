using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RyzenQuietPro
{
    public class DesktopIconItem
    {
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class DisplayInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class DesktopLayoutSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string DisplaySignature { get; set; } = string.Empty;
        public int DisplayCount { get; set; }
        public List<DisplayInfo> Displays { get; set; } = new();
        public List<DesktopIconItem> Icons { get; set; } = new();
    }

    public class DesktopIconsStorage
    {
        public string LastSavedSignature { get; set; } = string.Empty;
        public DateTime LastSavedTime { get; set; }
        public Dictionary<string, DesktopLayoutSnapshot> Profiles { get; set; } = new();
        public DesktopLayoutSnapshot? LatestSnapshot { get; set; }
    }

    public class DesktopSaveResult
    {
        public bool Success { get; set; }
        public int Count { get; set; }
        public int DisplayCount { get; set; }
        public string? Error { get; set; }
    }

    public class DesktopRestoreResult
    {
        public bool Success { get; set; }
        public int Restored { get; set; }
        public int Total { get; set; }
        public string? Error { get; set; }
    }

    public static class DesktopIconService
    {
        private static readonly object _fileLock = new();
        private static AppSettings? _settings;
        private static Action<string, bool>? _notifyCallback;
        private static System.Threading.Timer? _displayChangeDebounceTimer;
        private static bool _isSubscribed;

        private static readonly string StorageFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RyzenQuietPro"
        );
        private static readonly string StorageFile = Path.Combine(StorageFolder, "desktop_icons.json");

        #region Win32 P/Invoke

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, int dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? title);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int GWL_STYLE = -16;
        private const int LVS_AUTOARRANGE = 0x0100;

        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;
        private const uint LVM_SETITEMPOSITION32 = LVM_FIRST + 49;
        private const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;
        private const uint LVM_REDRAWITEMS = LVM_FIRST + 21;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LVITEM64
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        #endregion

        public static void Initialize(AppSettings settings, Action<string, bool>? onNotify)
        {
            _settings = settings;
            _notifyCallback = onNotify;

            if (!_isSubscribed)
            {
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                _isSubscribed = true;
            }
        }

        public static void UpdateSettings(AppSettings settings)
        {
            _settings = settings;
        }

        private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_settings == null || !_settings.AutoRestoreDesktopIcons) return;

            Logger.Log("DesktopIconService: DisplaySettingsChanged detected. Debouncing 1500ms before auto-restore...");

            _displayChangeDebounceTimer?.Dispose();
            _displayChangeDebounceTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (_settings == null || !_settings.AutoRestoreDesktopIcons) return;

                    string currentSig = GetDisplaySignature();
                    var storage = LoadStorage();
                    if (storage.Profiles.ContainsKey(currentSig))
                    {
                        Logger.Log($"DesktopIconService: Matching profile found for display signature '{currentSig}'. Auto-restoring icons...");
                        var result = RestoreLayout();
                        if (result.Success && _notifyCallback != null)
                        {
                            string msg = string.Format(Loc.Get("DesktopIcons_RestoredNotify"), result.Restored, result.Total);
                            _notifyCallback.Invoke(msg, true);
                        }
                    }
                    else
                    {
                        Logger.Log($"DesktopIconService: No saved profile for new signature '{currentSig}'. Skipping auto-restore.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"DesktopIconService auto-restore error: {ex.Message}");
                }
            }, null, 1500, Timeout.Infinite);
        }

        public static IntPtr FindDesktopListViewHandle()
        {
            IntPtr hProgman = FindWindow("Progman", null);
            IntPtr hView = FindWindowEx(hProgman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr hListView = IntPtr.Zero;

            if (hView != IntPtr.Zero)
            {
                hListView = FindWindowEx(hView, IntPtr.Zero, "SysListView32", null);
            }

            if (hListView == IntPtr.Zero)
            {
                EnumWindows((hWnd, lParam) =>
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetClassName(hWnd, sb, 256);
                    if (sb.ToString() == "WorkerW")
                    {
                        IntPtr v = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (v != IntPtr.Zero)
                        {
                            hListView = FindWindowEx(v, IntPtr.Zero, "SysListView32", null);
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }

            // Desktop thread fallback (e.g., if called from non-interactive background thread)
            if (hListView == IntPtr.Zero)
            {
                try
                {
                    IntPtr hDesk = OpenDesktop("Default", 0, false, 0x01FF);
                    if (hDesk != IntPtr.Zero)
                    {
                        EnumDesktopWindows(hDesk, (hWnd, lParam) =>
                        {
                            StringBuilder sb = new StringBuilder(256);
                            GetClassName(hWnd, sb, 256);
                            string cls = sb.ToString();
                            if (cls == "Progman" || cls == "WorkerW")
                            {
                                EnumChildWindows(hWnd, (hChild, lParam2) =>
                                {
                                    StringBuilder sbChild = new StringBuilder(256);
                                    GetClassName(hChild, sbChild, 256);
                                    if (sbChild.ToString() == "SysListView32")
                                    {
                                        hListView = hChild;
                                        return false;
                                    }
                                    return true;
                                }, IntPtr.Zero);
                            }
                            return hListView == IntPtr.Zero;
                        }, IntPtr.Zero);
                        CloseDesktop(hDesk);
                    }
                }
                catch { }
            }

            return hListView;
        }

        public static string GetDisplaySignature()
        {
            var screens = Screen.AllScreens
                .OrderBy(s => s.Bounds.X)
                .ThenBy(s => s.Bounds.Y)
                .ToList();

            return string.Join(";", screens.Select(s =>
                $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}{(s.Primary ? ":P" : "")}"));
        }

        public static List<DisplayInfo> GetCurrentDisplays()
        {
            return Screen.AllScreens
                .OrderBy(s => s.Bounds.X)
                .ThenBy(s => s.Bounds.Y)
                .Select(s => new DisplayInfo
                {
                    DeviceName = s.DeviceName,
                    X = s.Bounds.X,
                    Y = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    IsPrimary = s.Primary
                })
                .ToList();
        }

        public static string GetDisplaySummary()
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 1)
            {
                return $"{screens[0].Bounds.Width}x{screens[0].Bounds.Height}";
            }
            var parts = screens.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}");
            return $"{screens.Length} displays ({string.Join(", ", parts)})";
        }

        public static bool HasSavedSnapshot()
        {
            try
            {
                lock (_fileLock)
                {
                    if (!File.Exists(StorageFile)) return false;
                    var storage = LoadStorage();
                    return storage.LatestSnapshot != null || storage.Profiles.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static DesktopLayoutSnapshot? GetCurrentProfileSnapshot()
        {
            try
            {
                string sig = GetDisplaySignature();
                var storage = LoadStorage();
                if (storage.Profiles.TryGetValue(sig, out var profile))
                {
                    return profile;
                }
                return storage.LatestSnapshot;
            }
            catch
            {
                return null;
            }
        }

        public static string? GetLastSavedDateString()
        {
            try
            {
                var snapshot = GetCurrentProfileSnapshot();
                if (snapshot != null)
                {
                    return snapshot.Timestamp.ToString("yyyy-MM-dd");
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static string GetStatusInfo()
        {
            try
            {
                var snapshot = GetCurrentProfileSnapshot();
                if (snapshot == null)
                {
                    return Loc.Get("DesktopIcons_NoSnapshot");
                }
                string timeStr = snapshot.Timestamp.ToString("g");
                return string.Format(Loc.Get("DesktopIcons_StatusInfo"), snapshot.Icons.Count, snapshot.DisplayCount, timeStr);
            }
            catch
            {
                return Loc.Get("DesktopIcons_NoSnapshot");
            }
        }

        public static DesktopIconsStorage LoadStorage()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(StorageFile))
                    {
                        string json = File.ReadAllText(StorageFile);
                        return JsonSerializer.Deserialize<DesktopIconsStorage>(json) ?? new DesktopIconsStorage();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"DesktopIconService: LoadStorage error: {ex.Message}");
                }
                return new DesktopIconsStorage();
            }
        }

        private static void SaveStorage(DesktopIconsStorage storage)
        {
            lock (_fileLock)
            {
                try
                {
                    Directory.CreateDirectory(StorageFolder);
                    string json = JsonSerializer.Serialize(storage, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(StorageFile, json);
                }
                catch (Exception ex)
                {
                    Logger.Log($"DesktopIconService: SaveStorage error: {ex.Message}");
                }
            }
        }

        public static DesktopSaveResult SaveLayout()
        {
            IntPtr hListView = FindDesktopListViewHandle();
            if (hListView == IntPtr.Zero)
            {
                return new DesktopSaveResult { Success = false, Error = "Desktop ListView window not found" };
            }

            uint pid;
            GetWindowThreadProcessId(hListView, out pid);
            if (pid == 0)
            {
                return new DesktopSaveResult { Success = false, Error = "Unable to get Explorer process ID" };
            }

            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                return new DesktopSaveResult { Success = false, Error = $"OpenProcess failed (Error: {Marshal.GetLastWin32Error()})" };
            }

            try
            {
                int count = (int)SendMessage(hListView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0)
                {
                    return new DesktopSaveResult { Success = false, Error = "Desktop icon list is empty" };
                }

                uint allocSize = 4096;
                IntPtr pRemoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, allocSize, MEM_COMMIT, PAGE_READWRITE);
                if (pRemoteMem == IntPtr.Zero)
                {
                    return new DesktopSaveResult { Success = false, Error = "Failed to allocate memory in Explorer process" };
                }

                try
                {
                    IntPtr pRemotePoint = pRemoteMem;
                    IntPtr pRemoteLvItem = (IntPtr)((long)pRemoteMem + 64);
                    IntPtr pRemoteText = (IntPtr)((long)pRemoteMem + 256);

                    var icons = new List<DesktopIconItem>(count);

                    for (int i = 0; i < count; i++)
                    {
                        // 1. Get Position
                        SendMessage(hListView, LVM_GETITEMPOSITION, (IntPtr)i, pRemotePoint);
                        byte[] pointBytes = new byte[Marshal.SizeOf<POINT>()];
                        IntPtr bytesRead;
                        ReadProcessMemory(hProcess, pRemotePoint, pointBytes, (uint)pointBytes.Length, out bytesRead);
                        GCHandle handlePoint = GCHandle.Alloc(pointBytes, GCHandleType.Pinned);
                        POINT pt = Marshal.PtrToStructure<POINT>(handlePoint.AddrOfPinnedObject());
                        handlePoint.Free();

                        // 2. Get Text
                        LVITEM64 lv = new LVITEM64
                        {
                            iSubItem = 0,
                            cchTextMax = 260,
                            pszText = pRemoteText
                        };

                        byte[] lvBytes = new byte[Marshal.SizeOf<LVITEM64>()];
                        IntPtr ptrLv = Marshal.AllocHGlobal(lvBytes.Length);
                        Marshal.StructureToPtr(lv, ptrLv, false);
                        Marshal.Copy(ptrLv, lvBytes, 0, lvBytes.Length);
                        Marshal.FreeHGlobal(ptrLv);

                        IntPtr bytesWritten;
                        WriteProcessMemory(hProcess, pRemoteLvItem, lvBytes, (uint)lvBytes.Length, out bytesWritten);

                        SendMessage(hListView, LVM_GETITEMTEXTW, (IntPtr)i, pRemoteLvItem);

                        byte[] textBytes = new byte[520];
                        ReadProcessMemory(hProcess, pRemoteText, textBytes, (uint)textBytes.Length, out bytesRead);
                        string name = Encoding.Unicode.GetString(textBytes).Split('\0')[0].Trim();

                        if (!string.IsNullOrEmpty(name))
                        {
                            icons.Add(new DesktopIconItem
                            {
                                Name = name,
                                X = pt.x,
                                Y = pt.y
                            });
                        }
                    }

                    var displays = GetCurrentDisplays();
                    string signature = GetDisplaySignature();

                    var snapshot = new DesktopLayoutSnapshot
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Timestamp = DateTime.Now,
                        DisplaySignature = signature,
                        DisplayCount = displays.Count,
                        Displays = displays,
                        Icons = icons
                    };

                    var storage = LoadStorage();
                    storage.Profiles[signature] = snapshot;
                    storage.LatestSnapshot = snapshot;
                    storage.LastSavedSignature = signature;
                    storage.LastSavedTime = snapshot.Timestamp;
                    SaveStorage(storage);

                    Logger.Log($"DesktopIconService: Successfully saved {icons.Count} icons for {displays.Count} displays (Signature: {signature}).");

                    return new DesktopSaveResult
                    {
                        Success = true,
                        Count = icons.Count,
                        DisplayCount = displays.Count
                    };
                }
                finally
                {
                    VirtualFreeEx(hProcess, pRemoteMem, 0, MEM_RELEASE);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        public static DesktopRestoreResult RestoreLayout()
        {
            var storage = LoadStorage();
            string currentSig = GetDisplaySignature();

            DesktopLayoutSnapshot? snapshot = null;
            if (storage.Profiles.TryGetValue(currentSig, out var exactProfile))
            {
                snapshot = exactProfile;
            }
            else if (storage.LatestSnapshot != null)
            {
                snapshot = storage.LatestSnapshot;
            }

            if (snapshot == null || snapshot.Icons.Count == 0)
            {
                return new DesktopRestoreResult { Success = false, Error = Loc.Get("DesktopIcons_NoSnapshot") };
            }

            IntPtr hListView = FindDesktopListViewHandle();
            if (hListView == IntPtr.Zero)
            {
                return new DesktopRestoreResult { Success = false, Error = "Desktop ListView window not found" };
            }

            // Check if AutoArrange is on; if so, temporarily turn it off so icons can be placed
            int style = GetWindowLong(hListView, GWL_STYLE);
            bool hadAutoArrange = (style & LVS_AUTOARRANGE) != 0;
            if (hadAutoArrange)
            {
                SetWindowLong(hListView, GWL_STYLE, style & ~LVS_AUTOARRANGE);
            }

            uint pid;
            GetWindowThreadProcessId(hListView, out pid);
            if (pid == 0)
            {
                return new DesktopRestoreResult { Success = false, Error = "Unable to get Explorer process ID" };
            }

            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                return new DesktopRestoreResult { Success = false, Error = $"OpenProcess failed (Error: {Marshal.GetLastWin32Error()})" };
            }

            try
            {
                int count = (int)SendMessage(hListView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0)
                {
                    return new DesktopRestoreResult { Success = false, Error = "Desktop icon list is empty" };
                }

                uint allocSize = 4096;
                IntPtr pRemoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, allocSize, MEM_COMMIT, PAGE_READWRITE);
                if (pRemoteMem == IntPtr.Zero)
                {
                    return new DesktopRestoreResult { Success = false, Error = "Failed to allocate memory in Explorer process" };
                }

                try
                {
                    IntPtr pRemotePoint = pRemoteMem;
                    IntPtr pRemoteLvItem = (IntPtr)((long)pRemoteMem + 64);
                    IntPtr pRemoteText = (IntPtr)((long)pRemoteMem + 256);

                    // Build lookup map by icon name (case-insensitive)
                    var savedMap = new Dictionary<string, DesktopIconItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var ic in snapshot.Icons)
                    {
                        if (!savedMap.ContainsKey(ic.Name))
                        {
                            savedMap[ic.Name] = ic;
                        }
                    }

                    int restoredCount = 0;

                    for (int i = 0; i < count; i++)
                    {
                        // Get current item text
                        LVITEM64 lv = new LVITEM64
                        {
                            iSubItem = 0,
                            cchTextMax = 260,
                            pszText = pRemoteText
                        };

                        byte[] lvBytes = new byte[Marshal.SizeOf<LVITEM64>()];
                        IntPtr ptrLv = Marshal.AllocHGlobal(lvBytes.Length);
                        Marshal.StructureToPtr(lv, ptrLv, false);
                        Marshal.Copy(ptrLv, lvBytes, 0, lvBytes.Length);
                        Marshal.FreeHGlobal(ptrLv);

                        IntPtr bytesWritten;
                        WriteProcessMemory(hProcess, pRemoteLvItem, lvBytes, (uint)lvBytes.Length, out bytesWritten);

                        SendMessage(hListView, LVM_GETITEMTEXTW, (IntPtr)i, pRemoteLvItem);

                        byte[] textBytes = new byte[520];
                        IntPtr bytesRead;
                        ReadProcessMemory(hProcess, pRemoteText, textBytes, (uint)textBytes.Length, out bytesRead);
                        string name = Encoding.Unicode.GetString(textBytes).Split('\0')[0].Trim();

                        if (savedMap.TryGetValue(name, out var savedItem))
                        {
                            // Write POINT to remote memory
                            POINT pt = new POINT { x = savedItem.X, y = savedItem.Y };
                            byte[] pointBytes = new byte[Marshal.SizeOf<POINT>()];
                            IntPtr ptrPt = Marshal.AllocHGlobal(pointBytes.Length);
                            Marshal.StructureToPtr(pt, ptrPt, false);
                            Marshal.Copy(ptrPt, pointBytes, 0, pointBytes.Length);
                            Marshal.FreeHGlobal(ptrPt);

                            WriteProcessMemory(hProcess, pRemotePoint, pointBytes, (uint)pointBytes.Length, out bytesWritten);

                            // Send LVM_SETITEMPOSITION32 (supports 32-bit signed coordinates for multi-monitor)
                            IntPtr res = SendMessage(hListView, LVM_SETITEMPOSITION32, (IntPtr)i, pRemotePoint);
                            if (res == IntPtr.Zero)
                            {
                                // Fallback to LVM_SETITEMPOSITION if 32 is not handled
                                IntPtr lParamPos = (IntPtr)((savedItem.X & 0xFFFF) | ((savedItem.Y & 0xFFFF) << 16));
                                SendMessage(hListView, LVM_SETITEMPOSITION, (IntPtr)i, lParamPos);
                            }

                            restoredCount++;
                        }
                    }

                    // Refresh ListView
                    SendMessage(hListView, LVM_REDRAWITEMS, IntPtr.Zero, (IntPtr)(count - 1));
                    InvalidateRect(hListView, IntPtr.Zero, true);
                    UpdateWindow(hListView);

                    Logger.Log($"DesktopIconService: Restored {restoredCount} of {snapshot.Icons.Count} saved desktop icons.");

                    return new DesktopRestoreResult
                    {
                        Success = true,
                        Restored = restoredCount,
                        Total = snapshot.Icons.Count
                    };
                }
                finally
                {
                    VirtualFreeEx(hProcess, pRemoteMem, 0, MEM_RELEASE);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
    }
}

