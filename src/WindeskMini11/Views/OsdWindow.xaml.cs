using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WindeskMini11.Views
{
    /// <summary>
    /// 快捷键触发的置顶悬浮提示（OSD）。
    /// 无边框、不抢焦点、不出现在任务栏与 Alt+Tab，鼠标点击可穿透到下层。
    /// </summary>
    public partial class OsdWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private int _generation;

        public OsdWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => ApplyNoActivateStyle();
        }

        /// <summary>最后一次算出的屏幕坐标（逻辑像素），供自检读取。</summary>
        public string LastPlacement { get; private set; } = string.Empty;

        /// <summary>自检模式：真实走显示/动画/隐藏流程，但把窗口挪到屏幕外。</summary>
        public bool OffscreenMode { get; set; }

        public void ShowOsd(string title, string subtitle, bool dimmed)
        {
            ApplyContent(title, subtitle, dimmed);

            Opacity = 1; // 窗口整体透明由 Stage 动画控制
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            UpdateLayout();
            PlaceAtCursor();

            _generation++;

            Stage.BeginAnimation(OpacityProperty, null);
            StageShift.BeginAnimation(TranslateTransform.YProperty, null);

            Stage.Opacity = 0;
            StageShift.Y = 10;

            Stage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            StageShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(230))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        public void HideOsd()
        {
            if (!IsVisible) return;

            var token = ++_generation;
            var fade = new DoubleAnimation(Stage.Opacity, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fade.Completed += (_, _) =>
            {
                if (token == _generation) Hide();
            };

            Stage.BeginAnimation(OpacityProperty, fade);
            StageShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(StageShift.Y, -6, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
        }

        private void ApplyContent(string title, string subtitle, bool dimmed)
        {
            TitleText.Text = title;
            SubText.Text = subtitle;

            var brushKey = dimmed ? "TextTertiaryBrush" : "AccentBrush";
            CellA.SetResourceReference(Border.BackgroundProperty, brushKey);
            CellB.SetResourceReference(Border.BackgroundProperty, brushKey);
            CellC.SetResourceReference(Border.BackgroundProperty, brushKey);
            CellD.SetResourceReference(Border.BackgroundProperty, brushKey);
            SlashBar.Visibility = dimmed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyNoActivateStyle()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        }

        /// <summary>供自检读取窗口扩展样式（置顶 / 不激活 / 工具栏窗口 / 点击穿透）。</summary>
        public string DescribeStyle()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return "句柄未创建";

            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            return "topmost=" + ((style & WS_EX_TOPMOST) != 0) +
                   " noactivate=" + ((style & WS_EX_NOACTIVATE) != 0) +
                   " toolwindow=" + ((style & WS_EX_TOOLWINDOW) != 0) +
                   " clickThrough=" + ((style & WS_EX_TRANSPARENT) != 0);
        }

        /// <summary>贴到鼠标所在显示器的工作区底部居中（按物理像素定位，跨屏不同缩放也准）。</summary>
        private void PlaceAtCursor()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            if (!TryComputePlacement(ActualWidth, ActualHeight, dpi, out var x, out var y)) return;

            if (OffscreenMode)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, -20000, -20000, 0, 0,
                    SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return;
            }

            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private bool TryComputePlacement(double widthDip, double heightDip, DpiScale dpi,
            out int x, out int y)
        {
            x = 0;
            y = 0;

            GetCursorPos(out var cursor);
            var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            var width = (int)Math.Ceiling(widthDip * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(heightDip * dpi.DpiScaleY);

            var work = info.rcWork;
            x = work.Left + (work.Right - work.Left - width) / 2;
            y = work.Bottom - height - (int)Math.Round(64 * dpi.DpiScaleY);

            LastPlacement = x + "," + y + " " + width + "x" + height +
                            " work=" + work.Left + "," + work.Top + "," + work.Right + "," + work.Bottom;

            return true;
        }

        /// <summary>
        /// 自检用：窗口刚创建就挪到屏幕外显示，只做布局与定位计算，
        /// 不会在你桌面上闪一下。
        /// </summary>
        public void ShowOffscreen(string title, string subtitle, bool dimmed)
        {
            ApplyContent(title, subtitle, dimmed);
            Stage.Opacity = 1;
            StageShift.Y = 0;

            var helper = new WindowInteropHelper(this);
            var hwnd = helper.EnsureHandle();
            SetWindowPos(hwnd, HWND_TOPMOST, -20000, -20000, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

            Show();
            UpdateLayout();

            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                UpdateLayout();
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            TryComputePlacement(ActualWidth, ActualHeight, dpi, out _, out _);
        }
    }
}
