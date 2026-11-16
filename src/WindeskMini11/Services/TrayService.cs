using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace WindeskMini11.Services
{
    /// <summary>
    /// 托盘图标。用 Shell_NotifyIcon 直接实现，不再依赖 WinForms
    /// （WinForms 全家要占 20MB 左右，只为一个托盘图标不值）。
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        private const int WM_TRAYICON = 0x8000 + 1;

        private const int NIM_ADD = 0;
        private const int NIM_MODIFY = 1;
        private const int NIM_DELETE = 2;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;

        private const int NIIF_INFO = 0x00000001;

        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public int dwState;
            public int dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public int uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
            IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);

        private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

        private readonly HwndSource _sink;
        private readonly ContextMenu _menu;
        private MenuItem _desktopItem = null!;
        private readonly int _taskbarCreated;
        private readonly string _tooltip = "桌面美化 · Windesk Mini";

        private IntPtr _hIcon;
        private bool _ownsIcon;
        private bool _added;
        private bool _disposed;

        public event Action? OpenRequested;
        public event Action? ExitRequested;
        public event Action? ToggleDesktopRequested;
        public event Action? RestartExplorerRequested;

        public TrayService()
        {
            var parameters = new HwndSourceParameters("WindeskMini11.TraySink")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0
            };

            _sink = new HwndSource(parameters);
            _sink.AddHook(WndProc);

            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

            _menu = BuildMenu();
            AddIcon();
        }

        // ===== 图标 =====

        private void AddIcon()
        {
            if (_hIcon == IntPtr.Zero) _hIcon = LoadAppIcon();

            var data = CreateData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;

            _added = Shell_NotifyIcon(NIM_ADD, ref data);

            if (!_added)
            {
                Log.Write("托盘图标添加失败: " + Marshal.GetLastWin32Error());
            }
        }

        private NOTIFYICONDATA CreateData()
        {
            return new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _sink.Handle,
                uID = 1,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = _tooltip,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
        }

        private IntPtr LoadAppIcon()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var small = new IntPtr[1];
                    if (ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                    {
                        _ownsIcon = true;
                        return small[0];
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("取托盘图标失败: " + ex.Message);
            }

            return LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        }

        // ===== 菜单 =====

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayContextMenu");

            _desktopItem = CreateItem("隐藏桌面图标", () => ToggleDesktopRequested?.Invoke());

            menu.Items.Add(CreateItem("打开主界面", () => OpenRequested?.Invoke()));
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(_desktopItem);
            menu.Items.Add(CreateItem("重启资源管理器", () => RestartExplorerRequested?.Invoke()));
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateItem("退出", () => ExitRequested?.Invoke()));

            return menu;
        }

        private static Separator CreateSeparator()
        {
            var separator = new Separator();
            separator.SetResourceReference(FrameworkElement.StyleProperty, "TraySeparator");
            return separator;
        }

        private static MenuItem CreateItem(string header, Action onClick)
        {
            var item = new MenuItem { Header = header };
            item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem");
            item.Click += (_, _) => onClick();
            return item;
        }

        private void ShowMenu()
        {
            _desktopItem.Header = DesktopService.IconsVisible ? "隐藏桌面图标" : "显示桌面图标";

            // 先抢前台，否则点菜单外部时 WPF 收不到消息、菜单关不掉
            SetForegroundWindow(_sink.Handle);

            _menu.Placement = PlacementMode.MousePoint;
            _menu.IsOpen = false;
            _menu.IsOpen = true;
        }

        // ===== 消息 =====

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                var mouseMessage = lParam.ToInt32() & 0xFFFF;

                if (mouseMessage is WM_RBUTTONUP or WM_CONTEXTMENU)
                {
                    ShowMenu();
                    handled = true;
                }
                else if (mouseMessage == WM_LBUTTONDBLCLK)
                {
                    OpenRequested?.Invoke();
                    handled = true;
                }

                return IntPtr.Zero;
            }

            // Explorer 重启后托盘会被清空，需要重新添加（我们自己就会重启 explorer）
            if (_taskbarCreated != 0 && msg == _taskbarCreated)
            {
                Log.Write("托盘: Explorer 重启，重新添加图标");
                _added = false;
                AddIcon();
            }

            return IntPtr.Zero;
        }

        // ===== 对外 =====

        public void UpdateDesktopState(bool iconsVisible)
        {
            _desktopItem.Header = iconsVisible ? "隐藏桌面图标" : "显示桌面图标";
        }

        public void ShowBalloon(string title, string text)
        {
            if (_disposed || !_added) return;

            try
            {
                var data = CreateData();
                data.uFlags = NIF_INFO;
                data.szInfo = text;
                data.szInfoTitle = title;
                data.dwInfoFlags = NIIF_INFO;
                data.uTimeoutOrVersion = 4000;

                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
            catch (Exception ex)
            {
                Log.Write("托盘气泡失败: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_added)
                {
                    var data = CreateData();
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                    _added = false;
                }

                _menu.IsOpen = false;

                if (_ownsIcon && _hIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hIcon);
                }

                _sink.RemoveHook(WndProc);
                _sink.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("释放托盘失败: " + ex.Message);
            }
        }
    }
}
