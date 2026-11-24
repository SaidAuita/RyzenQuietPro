using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    /// <summary>
    /// Resident service for Punto Switcher style in-place keyboard layout switching.
    /// Converts text between English (QWERTY) and Russian (ЙЦУКЕН) layouts.
    /// Uses low-level keyboard hook (WH_KEYBOARD_LL) for 100% reliable hotkey capture (including Pause/Break).
    /// </summary>
    public static class KeyboardLayoutService
    {
        // Win32 Virtual Key Constants
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_MENU = 0x12; // Alt
        private const byte VK_LWIN = 0x5B;
        private const byte VK_RWIN = 0x5C;
        private const byte VK_C = 0x43;
        private const byte VK_V = 0x56;
        private const byte VK_A = 0x41;
        private const byte VK_PAUSE = 0x13;
        private const byte VK_CANCEL = 0x03; // Windows sends VK_CANCEL on Ctrl+Pause/Break
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Win32 Clipboard Constants & APIs
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        // Low-Level Keyboard Hook Constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc? _hookProc;
        private static IntPtr _hookId = IntPtr.Zero;
        private static AppSettings? _settingsRef;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        // Hardware mapping tables between QWERTY (EN) and ЙЦУКЕН (RU)
        private static readonly string EnLower = "`1234567890-=\\qwertyuiop[]asdfghjkl;'zxcvbnm,./";
        private static readonly string RuLower = "ё1234567890-=\\йцукенгшщзхъфывапролджэячсмитьбю.";

        private static readonly string EnUpper = "~!@#$%^&*()_+|QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
        private static readonly string RuUpper = "Ё!\"№;%:?*()_+/ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";

        private static readonly Dictionary<char, char> EnToRuMap = new();
        private static readonly Dictionary<char, char> RuToEnMap = new();

        static KeyboardLayoutService()
        {
            for (int i = 0; i < Math.Min(EnLower.Length, RuLower.Length); i++)
            {
                EnToRuMap[EnLower[i]] = RuLower[i];
                RuToEnMap[RuLower[i]] = EnLower[i];
            }

            for (int i = 0; i < Math.Min(EnUpper.Length, RuUpper.Length); i++)
            {
                EnToRuMap[EnUpper[i]] = RuUpper[i];
                RuToEnMap[RuUpper[i]] = EnUpper[i];
            }
        }

        #region Hook Management

        public static void StartHook(AppSettings settings)
        {
            StopHook();
            bool isAnyHookActive = settings.LayoutSwitchEnabled ||
                                   settings.HotkeyTranslateEnabled ||
                                   settings.HotkeyOcrEnabled ||
                                   settings.HotkeyOcrTranslateEnabled;

            if (!isAnyHookActive) return;

            _settingsRef = settings;
            _hookProc = HookCallback;

            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr moduleHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);

                if (_hookId != IntPtr.Zero)
                {
                    Logger.Log($"[LayoutHook] Low-level keyboard hook active (Layout={settings.LayoutSwitchEnabled}, Translate={settings.HotkeyTranslateEnabled}, OCR={settings.HotkeyOcrEnabled}, OCR+Trans={settings.HotkeyOcrTranslateEnabled}).");
                }
                else
                {
                    Logger.Log($"[LayoutHook] Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[LayoutHook] Error installing hook: {ex.Message}");
            }
        }

        public static void StopHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                try
                {
                    UnhookWindowsHookEx(_hookId);
                }
                catch { }
                _hookId = IntPtr.Zero;
                _hookProc = null;
                Logger.Log("[LayoutHook] Low-level keyboard hook uninstalled.");
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                if (_settingsRef != null)
                {
                    try
                    {
                        var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                        bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                        bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                        bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                        bool winDown = ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);

                        // Note: if kbd.vkCode == VK_CANCEL, Ctrl is physically pressed (Ctrl+Pause/Break)
                        if (kbd.vkCode == VK_CANCEL) ctrlDown = true;

                        // 1. In-place Keyboard Layout Switcher
                        if (_settingsRef.LayoutSwitchEnabled)
                        {
                            uint targetVk = (uint)_settingsRef.LayoutSwitchVirtualKey;
                            if (targetVk == 0) targetVk = (uint)Keys.Pause;

                            bool keyMatch = (kbd.vkCode == targetVk);
                            if (!keyMatch && targetVk == (uint)Keys.Pause)
                            {
                                keyMatch = (kbd.vkCode == VK_PAUSE || kbd.vkCode == VK_CANCEL);
                            }

                            if (keyMatch)
                            {
                                bool reqCtrl = _settingsRef.LayoutSwitchModifiersCtrl;
                                bool reqAlt = _settingsRef.LayoutSwitchModifiersAlt;
                                bool reqShift = _settingsRef.LayoutSwitchModifiersShift;
                                bool reqWin = _settingsRef.LayoutSwitchModifiersWin;

                                if (ctrlDown == reqCtrl && altDown == reqAlt && shiftDown == reqShift && winDown == reqWin)
                                {
                                    Logger.Log($"[LayoutHook] Hotkey triggered! vk=0x{kbd.vkCode:X}, Ctrl={ctrlDown}, Alt={altDown}, Shift={shiftDown}, Win={winDown}");
                                    Task.Run(() => SwitchSelectedTextLayout(IntPtr.Zero));
                                    return (IntPtr)1; // Consume key event
                                }
                            }
                        }

                        // 2. Hotkey 1: Translate Selected Text
                        if (_settingsRef.HotkeyTranslateEnabled && _settingsRef.HotkeyTranslateKey != 0)
                        {
                            if (kbd.vkCode == (uint)_settingsRef.HotkeyTranslateKey)
                            {
                                if (ctrlDown == _settingsRef.HotkeyTranslateCtrl &&
                                    altDown == _settingsRef.HotkeyTranslateAlt &&
                                    shiftDown == _settingsRef.HotkeyTranslateShift &&
                                    winDown == _settingsRef.HotkeyTranslateWin)
                                {
                                    IntPtr fg = GetForegroundWindow();
                                    Logger.Log($"[LayoutHook] HotkeyTranslate triggered!");
                                    Task.Run(() => TriggerTranslateSelectedText(fg));
                                    return (IntPtr)1;
                                }
                            }
                        }

                        // 3. Hotkey 2: Screen OCR Only
                        if (_settingsRef.HotkeyOcrEnabled && _settingsRef.HotkeyOcrKey != 0)
                        {
                            if (kbd.vkCode == (uint)_settingsRef.HotkeyOcrKey)
                            {
                                if (ctrlDown == _settingsRef.HotkeyOcrCtrl &&
                                    altDown == _settingsRef.HotkeyOcrAlt &&
                                    shiftDown == _settingsRef.HotkeyOcrShift &&
                                    winDown == _settingsRef.HotkeyOcrWin)
                                {
                                    IntPtr fg = GetForegroundWindow();
                                    Logger.Log($"[LayoutHook] HotkeyOcr triggered!");
                                    Task.Run(() => TriggerScreenSnipper(fg, andTranslate: false));
                                    return (IntPtr)1;
                                }
                            }
                        }

                        // 4. Hotkey 3: Screen OCR + Translate
                        if (_settingsRef.HotkeyOcrTranslateEnabled && _settingsRef.HotkeyOcrTranslateKey != 0)
                        {
                            if (kbd.vkCode == (uint)_settingsRef.HotkeyOcrTranslateKey)
                            {
                                if (ctrlDown == _settingsRef.HotkeyOcrTranslateCtrl &&
                                    altDown == _settingsRef.HotkeyOcrTranslateAlt &&
                                    shiftDown == _settingsRef.HotkeyOcrTranslateShift &&
                                    winDown == _settingsRef.HotkeyOcrTranslateWin)
                                {
                                    IntPtr fg = GetForegroundWindow();
                                    Logger.Log($"[LayoutHook] HotkeyOcrTranslate triggered!");
                                    Task.Run(() => TriggerScreenSnipper(fg, andTranslate: true));
                                    return (IntPtr)1;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[LayoutHook] Callback error: {ex.Message}");
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public static void TriggerTranslateSelectedText(IntPtr targetHwnd)
        {
            try
            {
                ClearClipboard();
                SimulateCopy();

                string grabbedText = string.Empty;
                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(25);
                    grabbedText = GetClipboardText();
                    if (!string.IsNullOrEmpty(grabbedText)) break;
                }

                if (string.IsNullOrWhiteSpace(grabbedText))
                {
                    grabbedText = GetClipboardText();
                }

                if (!string.IsNullOrWhiteSpace(grabbedText))
                {
                    ShowTranslationPopup(grabbedText, null, isOcr: false, andTranslate: false, targetHwnd);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[LayoutHook] TriggerTranslateSelectedText error: {ex.Message}");
            }
        }

        public static void TriggerScreenSnipper(IntPtr targetHwnd, bool andTranslate)
        {
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0] is Form mainForm && mainForm.IsHandleCreated)
            {
                mainForm.BeginInvoke(new Action(() =>
                {
                    var snipper = new ScreenSnipperForm(
                        onCaptured: pngBytes =>
                        {
                            ShowTranslationPopup(null, pngBytes, isOcr: true, andTranslate: andTranslate, targetHwnd);
                        },
                        onCancelled: () => { }
                    );
                    snipper.Show();
                }));
            }
        }

        public static void ShowTranslationPopup(string? text, byte[]? pngBytes, bool isOcr, bool andTranslate, IntPtr targetHwnd)
        {
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0] is Form mainForm && mainForm.IsHandleCreated)
            {
                mainForm.BeginInvoke(new Action(() =>
                {
                    var popup = new TranslationPopupForm(text, pngBytes, isOcr, andTranslate, targetHwnd);
                    popup.Show();
                    popup.Activate();
                }));
            }
        }

        #endregion

        #region Text Conversion

        public static string ConvertEnToRu(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (EnToRuMap.TryGetValue(c, out char ru))
                    sb.Append(ru);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string ConvertRuToEn(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (RuToEnMap.TryGetValue(c, out char en))
                    sb.Append(en);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string ToggleLayout(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            int latCount = 0;
            int cyrCount = 0;

            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    latCount++;
                else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё')
                    cyrCount++;
            }

            if (latCount >= cyrCount && latCount > 0)
            {
                return ConvertEnToRu(text);
            }
            else if (cyrCount > 0)
            {
                return ConvertRuToEn(text);
            }

            // Fallback: character-by-character toggle
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (EnToRuMap.TryGetValue(c, out char ru))
                    sb.Append(ru);
                else if (RuToEnMap.TryGetValue(c, out char en))
                    sb.Append(en);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        #endregion

        #region Clipboard Operations

        public static string GetClipboardText()
        {
            for (int i = 0; i < 10; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                        if (handle != IntPtr.Zero)
                        {
                            IntPtr ptr = GlobalLock(handle);
                            if (ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    return Marshal.PtrToStringUni(ptr) ?? string.Empty;
                                }
                                finally
                                {
                                    GlobalUnlock(handle);
                                }
                            }
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(20);
            }
            return string.Empty;
        }

        public static bool SetClipboardText(string text)
        {
            if (text == null) text = string.Empty;
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");

            for (int i = 0; i < 10; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                        if (hMem != IntPtr.Zero)
                        {
                            IntPtr ptr = GlobalLock(hMem);
                            if (ptr != IntPtr.Zero)
                            {
                                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                                GlobalUnlock(hMem);
                                SetClipboardData(CF_UNICODETEXT, hMem);
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(20);
            }
            return false;
        }

        #endregion

        #region In-Place Layout Switch Action

        private static readonly object _switchLock = new();

        public static void ClearClipboard()
        {
            for (int i = 0; i < 5; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                        return;
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(10);
            }
        }

        private static void SimulateSelectAll()
        {
            Thread.Sleep(30);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_A, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
            keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void SimulateCopy()
        {
            Thread.Sleep(30);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void SimulatePaste()
        {
            Thread.Sleep(40);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void SwitchSelectedTextLayout(IntPtr targetHwnd = default)
        {
            lock (_switchLock)
            {
                try
                {
                    // 1. If triggered from menu, restore focus to target app window
                    if (targetHwnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(targetHwnd);
                        Thread.Sleep(100);
                    }

                    // 2. Clear clipboard so we can definitively detect if Ctrl+C copied anything
                    ClearClipboard();

                    // 3. First attempt: simulate Ctrl+C to copy existing selection
                    SimulateCopy();

                    // 4. Poll clipboard for selection (up to 120ms)
                    string grabbedText = string.Empty;
                    for (int i = 0; i < 6; i++)
                    {
                        Thread.Sleep(20);
                        string cur = GetClipboardText();
                        if (!string.IsNullOrEmpty(cur))
                        {
                            grabbedText = cur;
                            break;
                        }
                    }

                    // 5. If no text was selected (clipboard still empty), select all text (Ctrl+A) and copy
                    if (string.IsNullOrEmpty(grabbedText))
                    {
                        Logger.Log("[Layout] No text selected. Selecting all text in active window (Ctrl+A)...");
                        SimulateSelectAll();
                        SimulateCopy();

                        for (int i = 0; i < 10; i++)
                        {
                            Thread.Sleep(20);
                            string cur = GetClipboardText();
                            if (!string.IsNullOrEmpty(cur))
                            {
                                grabbedText = cur;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(grabbedText))
                    {
                        Logger.Log("[Layout] No text found in selection or active window.");
                        return;
                    }

                    // 6. Convert layout
                    string converted = ToggleLayout(grabbedText);
                    if (converted == grabbedText)
                    {
                        Logger.Log($"[Layout] Text '{grabbedText}' unchanged after conversion.");
                        return;
                    }

                    // 7. Put converted text into clipboard
                    if (!SetClipboardText(converted))
                    {
                        Logger.Log("[Layout] Failed to put converted text to clipboard.");
                        return;
                    }

                    // 8. Simulate Ctrl+V to paste in-place
                    SimulatePaste();

                    Logger.Log($"[Layout] Converted {grabbedText.Length} chars: '{grabbedText.Trim()}' -> '{converted.Trim()}'");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Layout] Error in SwitchSelectedTextLayout: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
