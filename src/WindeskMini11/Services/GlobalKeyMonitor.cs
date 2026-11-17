using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WindeskMini11.Services
{
    /// <summary>
    /// 全局键盘监听（WH_KEYBOARD_LL）。
    ///
    /// 存在的意义：RegisterHotKey 无法把「单独一个修饰键」（右 Alt / 左 Ctrl / 右 Shift …）
    /// 当成热键，这类需求只能靠自己监听按键。钩子只在两种场景下启用：
    ///   1. 录制快捷键时（捕获按键）；
    ///   2. 用户真的把热键设成了单个修饰键。
    /// 其余情况不安装钩子，避免不必要的全局键盘输入监听。
    ///
    /// 单击语义：修饰键按下后若期间按了别的键（例如 Alt+Tab），不会触发；
    /// 只有「按下 → 松开，中间没有其他按键」才算单击。
    /// </summary>
    public sealed class GlobalKeyMonitor : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint LLKHF_EXTENDED = 0x01;

        private const uint VK_ESCAPE = 0x1B;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint VkCode;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private readonly LowLevelKeyboardProc _proc;
        private readonly HashSet<uint> _pressed = new HashSet<uint>();
        private IntPtr _hook = IntPtr.Zero;

        private bool _capturing;
        private bool _captureDone;
        private int _watchVk;
        private uint _pendingModifier;
        private bool _cancelPending;

        /// <summary>录制到一组按键（形如 "Ctrl+Alt+D" / "RAlt"）。</summary>
        public event Action<string>? Captured;

        /// <summary>录制被取消（Esc）。</summary>
        public event Action? CaptureCancelled;

        /// <summary>被监听的单个修饰键被单独点击。</summary>
        public event Action? WatchedKeyTriggered;

        public GlobalKeyMonitor()
        {
            _proc = HookCallback;
        }

        public bool IsHooked => _hook != IntPtr.Zero;

        // ===== 录制 =====

        public void StartCapture()
        {
            _capturing = true;
            _captureDone = false;
            _pendingModifier = 0;
            _cancelPending = false;
            EnsureHook();
        }

        public void StopCapture()
        {
            _capturing = false;
            ReleaseIfIdle();
        }

        // ===== 监听单个修饰键 =====

        public void WatchModifierKey(uint vk)
        {
            _watchVk = (int)vk;
            EnsureHook();
        }

        public void StopWatching()
        {
            _watchVk = 0;
            ReleaseIfIdle();
        }

        /// <summary>
        /// 重新安装钩子。睡眠唤醒 / 锁屏解锁 / 会话切换后，系统可能把低级键盘钩子丢掉，
        /// 表现为"进程还在、热键不响应"，重装一次就好。
        /// </summary>
        public void ReinstallHook()
        {
            if (!_capturing && _watchVk == 0) return;

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _pressed.Clear();
            _pendingModifier = 0;
            _cancelPending = false;

            EnsureHook();
            Log.Write("键盘钩子重装: " + (IsHooked ? "成功" : "失败"));
        }

        /// <summary>定时自检：该有钩子却没有时补装。</summary>
        public void HealthCheck()
        {
            if (_capturing || _watchVk != 0)
            {
                if (_hook == IntPtr.Zero) EnsureHook();
            }
        }

        // ===== 钩子生命周期 =====

        private void EnsureHook()
        {
            if (_hook != IntPtr.Zero) return;

            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                Log.Write("键盘钩子安装失败: 错误码 " + Marshal.GetLastWin32Error());
            }
            else
            {
                Log.Write("键盘钩子已安装");
            }
        }

        private void ReleaseIfIdle()
        {
            if (_capturing || _watchVk != 0) return;
            if (_hook == IntPtr.Zero) return;

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _pressed.Clear();
            _pendingModifier = 0;
            _cancelPending = false;
            Log.Write("键盘钩子已卸载");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    HandleKey(wParam, lParam);
                }
                catch (Exception ex)
                {
                    Log.Write("键盘钩子处理异常: " + ex.Message);
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void HandleKey(IntPtr wParam, IntPtr lParam)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var vk = Canonicalize(info);
            var isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            var isModifier = IsModifierKey(vk);

            if (isDown)
            {
                if (!_pressed.Add(vk)) return; // 自动重复

                if (isModifier)
                {
                    if (_pressed.Count == 1)
                    {
                        _pendingModifier = vk;
                        _cancelPending = false;
                    }
                    else
                    {
                        // 已有别的键按下，这次不算"单击修饰键"
                        _cancelPending = true;
                    }

                    return;
                }

                // 普通按键：录制时立刻完成；同时取消修饰键单击判定
                if (_capturing) CompleteCapture(vk, false);
                _pendingModifier = 0;
                _cancelPending = true;
                return;
            }

            // 抬起
            _pressed.Remove(vk);
            if (!isModifier) return;

            var isCleanTap = _pendingModifier == vk && !_cancelPending && _pressed.Count == 0;
            if (_pendingModifier == vk) _pendingModifier = 0;
            if (_pressed.Count == 0) _cancelPending = false;

            if (!isCleanTap) return;

            if (_capturing)
            {
                CompleteCapture(vk, true);
            }
            else if (_watchVk != 0 && (int)vk == _watchVk)
            {
                Log.Write("单修饰键热键触发: " + (HotkeyDefinition.NameFromVk((int)vk) ?? vk.ToString("X")));
                WatchedKeyTriggered?.Invoke();
            }
        }

        private void CompleteCapture(uint vk, bool modifierOnly)
        {
            if (_captureDone) return;
            _captureDone = true;

            if (vk == VK_ESCAPE)
            {
                _capturing = false;
                ReleaseIfIdle();
                CaptureCancelled?.Invoke();
                return;
            }

            var text = BuildText(vk, modifierOnly);
            _capturing = false;
            ReleaseIfIdle();

            if (string.IsNullOrEmpty(text)) return;
            Log.Write("录制到快捷键: " + text);
            Captured?.Invoke(text);
        }

        private string? BuildText(uint vk, bool modifierOnly)
        {
            var name = HotkeyDefinition.NameFromVk((int)vk);
            if (name == null) return null;

            if (modifierOnly) return name;

            var ctrl = false;
            var alt = false;
            var shift = false;
            var win = false;

            foreach (var held in _pressed)
            {
                if (held == vk) continue;
                switch (held)
                {
                    case 0xA2:
                    case 0xA3:
                        ctrl = true;
                        break;
                    case 0xA4:
                    case 0xA5:
                        alt = true;
                        break;
                    case 0xA0:
                    case 0xA1:
                        shift = true;
                        break;
                    case 0x5B:
                    case 0x5C:
                        win = true;
                        break;
                }
            }

            var sb = new StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (alt) sb.Append("Alt+");
            if (shift) sb.Append("Shift+");
            if (win) sb.Append("Win+");
            return sb.Append(name).ToString();
        }

        /// <summary>把钩子给的 vk 规范化为具体左右键（0xA0–0xA5 / 0x5B / 0x5C）。</summary>
        private static uint Canonicalize(KbdLlHookStruct info)
        {
            var extended = (info.Flags & LLKHF_EXTENDED) != 0;

            switch (info.VkCode)
            {
                case 0x10: // VK_SHIFT
                    return info.ScanCode == 0x36 ? 0xA1u : 0xA0u;
                case 0x11: // VK_CONTROL
                    return extended ? 0xA3u : 0xA2u;
                case 0x12: // VK_MENU
                    return extended ? 0xA5u : 0xA4u;
                default:
                    return info.VkCode;
            }
        }

        public static bool IsModifierKey(uint vk)
        {
            return vk is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
        }

        public void Dispose()
        {
            _capturing = false;
            _watchVk = 0;
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _pressed.Clear();
        }
    }
}
