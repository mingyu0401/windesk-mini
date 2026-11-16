using System;
using System.Windows;
using System.Windows.Threading;
using WindeskMini11.Views;

namespace WindeskMini11.Services
{
    /// <summary>快捷键触发时的置顶悬浮提示。</summary>
    public static class OsdService
    {
        private const int DwellMs = 1500;

        private static OsdWindow? _window;
        private static DispatcherTimer? _dwell;

        public static bool Enabled => AppCore.Config.HotkeyOverlay;

        /// <summary>自检模式：走完整流程但显示在屏幕外。</summary>
        public static bool OffscreenMode { get; set; }

        public static bool IsShowing => _window != null && _window.IsVisible;

        public static string Placement => _window?.LastPlacement ?? string.Empty;

        public static string Style => _window?.DescribeStyle() ?? string.Empty;

        public static void ShowIconState(bool iconsVisible)
        {
            var hotkey = HotkeyDefinition.Friendly(AppCore.Config.Hotkey);

            if (iconsVisible)
            {
                Show("桌面图标已显示", hotkey + " · 再按一次隐藏", false);
            }
            else
            {
                Show("桌面图标已隐藏", hotkey + " · 再按一次恢复", true);
            }
        }

        public static void ShowDesktop()
        {
            var hotkey = HotkeyDefinition.Friendly(AppCore.Config.Hotkey);
            Show("已显示桌面", hotkey + " · 再次按下切换窗口", false);
        }

        public static void Show(string title, string subtitle, bool dimmed)
        {
            if (!Enabled) return;
            ShowCore(title, subtitle, dimmed);
        }

        /// <summary>界面上点「预览」用，不受开关限制。</summary>
        public static void Preview()
        {
            var hotkey = HotkeyDefinition.Friendly(AppCore.Config.Hotkey);
            ShowCore("桌面图标已隐藏", hotkey + " · 再按一次恢复", true);
        }

        private static void ShowCore(string title, string subtitle, bool dimmed)
        {
            var app = Application.Current;
            if (app == null) return;

            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => ShowCore(title, subtitle, dimmed)));
                return;
            }

            try
            {
                if (_window == null || !_window.IsLoaded)
                {
                    _window = new OsdWindow();
                }

                _window.OffscreenMode = OffscreenMode;
                _window.ShowOsd(title, subtitle, dimmed);

                if (_dwell == null)
                {
                    _dwell = new DispatcherTimer();
                    _dwell.Tick += OnDwell;
                }

                _dwell.Stop();
                _dwell.Interval = TimeSpan.FromMilliseconds(DwellMs);
                _dwell.Start();
            }
            catch (Exception ex)
            {
                Log.Write("显示悬浮提示失败: " + ex.Message);
            }
        }

        private static void OnDwell(object? sender, EventArgs e)
        {
            _dwell?.Stop();
            _window?.HideOsd();
        }

        public static void Close()
        {
            try
            {
                _dwell?.Stop();
                _dwell = null;

                if (_window != null)
                {
                    _window.Close();
                    _window = null;
                }
            }
            catch (Exception ex)
            {
                Log.Write("关闭悬浮提示失败: " + ex.Message);
            }
        }
    }
}
