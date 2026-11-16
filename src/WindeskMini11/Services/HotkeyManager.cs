using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WindeskMini11.Services
{
    /// <summary>热键文本 <-> 修饰键 / 虚拟键 的转换与格式化。</summary>
    public static class HotkeyDefinition
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        public static bool TryParse(string? text, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 1) return false;

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].ToLowerInvariant();
                var isLast = i == parts.Length - 1;

                if (part is "ctrl" or "control") modifiers |= MOD_CONTROL;
                else if (part is "alt") modifiers |= MOD_ALT;
                else if (part is "shift") modifiers |= MOD_SHIFT;
                else if (part is "win" or "windows" or "super") modifiers |= MOD_WIN;
                else if (isLast) vk = NameToVk(parts[i]);
                else return false;
            }

            // 支持单键（无修饰键），例如 F8、F12、Pause 等
            return vk != 0;
        }

        public static uint NameToVk(string name)
        {
            name = name.Trim();
            if (name.Length == 1)
            {
                var c = char.ToUpperInvariant(name[0]);
                if (c >= 'A' && c <= 'Z') return (uint)c;
                if (c >= '0' && c <= '9') return (uint)c;
            }

            if (name.Length is 2 or 3 && (name[0] is 'F' or 'f')
                && int.TryParse(name.AsSpan(1), out var fn) && fn >= 1 && fn <= 24)
            {
                return (uint)(0x70 + fn - 1);
            }

            if (name.Length == 4 && name.StartsWith("num", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan(3), out var num) && num >= 0 && num <= 9)
            {
                return (uint)(0x60 + num);
            }

            return name.ToLowerInvariant() switch
            {
                "space" => 0x20,
                "enter" or "return" => 0x0D,
                "tab" => 0x09,
                "esc" or "escape" => 0x1B,
                "spacebar" => 0x20,
                "printscreen" or "prtsc" => 0x2C,
                "insert" => 0x2D,
                "delete" or "del" => 0x2E,
                "home" => 0x24,
                "end" => 0x23,
                "pageup" => 0x21,
                "pagedown" => 0x22,
                "left" => 0x25,
                "up" => 0x26,
                "right" => 0x27,
                "down" => 0x28,
                "`" or "oem3" => 0xC0,
                "-" => 0xBD,
                "=" => 0xBB,
                "[" => 0xDB,
                "]" => 0xDD,
                ";" => 0xBA,
                "'" => 0xDE,
                "," => 0xBC,
                "." => 0xBE,
                "/" => 0xBF,
                "\\" => 0xDC,
                "lalt" or "leftalt" => 0xA4,
                "ralt" or "rightalt" => 0xA5,
                "lctrl" or "leftctrl" => 0xA2,
                "rctrl" or "rightctrl" => 0xA3,
                "lshift" or "leftshift" => 0xA0,
                "rshift" or "rightshift" => 0xA1,
                "lwin" or "leftwin" => 0x5B,
                "rwin" or "rightwin" => 0x5C,
                _ => 0
            };
        }

        public static string? NameFromVk(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);

            return vk switch
            {
                0x20 => "Space",
                0x0D => "Enter",
                0x09 => "Tab",
                0x1B => "Esc",
                0x2C => "PrintScreen",
                0x2D => "Insert",
                0x2E => "Delete",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0xA0 => "LShift",
                0xA1 => "RShift",
                0xA2 => "LCtrl",
                0xA3 => "RCtrl",
                0xA4 => "LAlt",
                0xA5 => "RAlt",
                0x5B => "LWin",
                0x5C => "RWin",
                _ => null
            };
        }

        /// <summary>是否"单独一个修饰键"（RegisterHotKey 注册不了，需要键盘钩子）。</summary>
        public static bool IsModifierOnly(uint vk)
        {
            return GlobalKeyMonitor.IsModifierKey(vk);
        }

        /// <summary>界面展示用的友好名称，例如 RAlt → 右 Alt。</summary>
        public static string Friendly(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            return text
                .Replace("RAlt", "右 Alt")
                .Replace("LAlt", "左 Alt")
                .Replace("RCtrl", "右 Ctrl")
                .Replace("LCtrl", "左 Ctrl")
                .Replace("RShift", "右 Shift")
                .Replace("LShift", "左 Shift")
                .Replace("RWin", "右 Win")
                .Replace("LWin", "左 Win");
        }

        /// <summary>从一次按键事件生成配置字符串，如 "Ctrl+Alt+D" 或单键 "F8"。</summary>
        public static string? FromKey(Key key, ModifierKeys mods)
        {
            var vk = KeyInterop.VirtualKeyFromKey(key);
            var name = NameFromVk(vk);
            if (name == null) return null;

            var sb = new StringBuilder();
            if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (mods.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");

            return sb.Append(name).ToString();
        }

        /// <summary>是否是无修饰键的单键热键（会被全局占用，需要提醒）。</summary>
        public static bool IsBareKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.IndexOf('+') < 0;
        }

        public static string Describe(uint modifiers, uint vk)
        {
            var sb = new StringBuilder();
            if ((modifiers & MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((modifiers & MOD_ALT) != 0) sb.Append("Alt+");
            if ((modifiers & MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((modifiers & MOD_WIN) != 0) sb.Append("Win+");
            sb.Append(NameFromVk((int)vk) ?? "?");
            return sb.ToString();
        }
    }

    /// <summary>全局热键注册与消息回调。</summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int HotkeyId = 0x4D31;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _handle = IntPtr.Zero;
        private HwndSource? _source;
        private int _lastError;

        public bool IsRegistered { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        /// <summary>单个修饰键热键需要它（由 AppCore 注入）。</summary>
        public GlobalKeyMonitor? Monitor { get; set; }

        public event Action? Pressed;

        public void Attach(Window window)
        {
            Attach(new WindowInteropHelper(window).Handle);
        }

        /// <summary>直接挂到已有窗口句柄（诊断用，无需显示窗口）。</summary>
        public void Attach(IntPtr handle)
        {
            _handle = handle;
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                Pressed?.Invoke();
            }

            return IntPtr.Zero;
        }

        public bool Register(string hotkeyText)
        {
            UnregisterHotKey();

            if (!HotkeyDefinition.TryParse(hotkeyText, out var mods, out var vk))
            {
                LastError = "热键格式无效";
                return false;
            }

            // 单个修饰键（右 Alt / 左 Ctrl …）RegisterHotKey 注册不了，改走键盘钩子
            if (mods == 0 && HotkeyDefinition.IsModifierOnly(vk))
            {
                if (Monitor == null)
                {
                    LastError = "当前环境不支持单修饰键热键";
                    return false;
                }

                Monitor.WatchedKeyTriggered -= OnMonitorTriggered;
                Monitor.WatchedKeyTriggered += OnMonitorTriggered;
                Monitor.WatchModifierKey(vk);
                IsRegistered = Monitor.IsHooked;
                LastError = IsRegistered ? string.Empty : "键盘钩子安装失败";
                Log.Write("注册单修饰键热键 " + hotkeyText + " => " + IsRegistered + " " + LastError);
                return IsRegistered;
            }

            if (_handle == IntPtr.Zero)
            {
                LastError = "窗口句柄未就绪";
                return false;
            }

            IsRegistered = RegisterHotKey(_handle, HotkeyId, mods | MOD_NOREPEAT, vk);
            if (IsRegistered)
            {
                LastError = string.Empty;
            }
            else
            {
                _lastError = Marshal.GetLastWin32Error();
                LastError = _lastError == 1409 ? "该热键已被其他程序占用" : "注册热键失败（错误码 " + _lastError + "）";
            }

            Log.Write("注册热键 " + hotkeyText + " => " + IsRegistered + " " + LastError);
            return IsRegistered;
        }

        private void OnMonitorTriggered()
        {
            Pressed?.Invoke();
        }

        public void UnregisterHotKey()
        {
            if (_handle != IntPtr.Zero && IsRegistered)
            {
                UnregisterHotKey(_handle, HotkeyId);
            }

            if (Monitor != null)
            {
                Monitor.WatchedKeyTriggered -= OnMonitorTriggered;
                Monitor.StopWatching();
            }

            IsRegistered = false;
        }

        public void Dispose()
        {
            UnregisterHotKey();
            _source?.RemoveHook(WndProc);
            _source = null;
        }
    }
}
