using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindeskMini11.Services;

namespace WindeskMini11.Views
{
    public partial class DesktopPage : UserControl, IRefreshablePage
    {
        private bool _suppress;
        private bool _recording;
        private Window? _window;

        public DesktopPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private IPageHost? Host => Window.GetWindow(this) as IPageHost;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _window = Window.GetWindow(this);
            AppCore.DesktopStateChanged -= OnDesktopStateChanged;
            AppCore.DesktopStateChanged += OnDesktopStateChanged;
            RefreshState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppCore.DesktopStateChanged -= OnDesktopStateChanged;
            StopRecording();
        }

        private void OnDesktopStateChanged()
        {
            Dispatcher.BeginInvoke(new Action(UpdateIconState));
        }

        public void RefreshState()
        {
            _suppress = true;
            try
            {
                var cfg = AppCore.Config;
                HotkeyToggle.IsChecked = cfg.HotkeyEnabled;
                HotkeyChipText.Text = HotkeyDefinition.Friendly(cfg.Hotkey);
                ActionToggleIcons.IsChecked = cfg.HotkeyAction == HotkeyAction.ToggleDesktopIcons;
                ActionShowDesktop.IsChecked = cfg.HotkeyAction == HotkeyAction.ShowDesktop;
                TrayToggle.IsChecked = cfg.CloseToTray;
                AutoStartToggle.IsChecked = cfg.AutoStart;
                OverlayToggle.IsChecked = cfg.HotkeyOverlay;

                HotkeyHint.Text = cfg.HotkeyEnabled
                    ? "点击「更改」后按下新的组合键，也支持单个按键（F7、右 Alt 等）；Esc 取消。"
                    : "快捷键已关闭，打开上方开关后才会生效。";

                UpdateIconState();
            }
            finally
            {
                _suppress = false;
            }
        }

        private void UpdateIconState()
        {
            var visible = DesktopService.IconsVisible;
            IconStateText.Text = visible ? "显示中" : "已隐藏";
            ToggleIconsBtn.Content = visible ? "隐藏图标" : "显示图标";
            AppCore.Tray?.UpdateDesktopState(visible);
        }

        // ===== 桌面图标切换 =====

        private void ToggleIconsBtn_Click(object sender, RoutedEventArgs e)
        {
            var target = !DesktopService.IconsVisible;
            if (!DesktopService.SetIconsVisible(target))
            {
                Host?.ShowToast("未找到桌面图标容器，请尝试重启资源管理器", false);
                return;
            }

            UpdateIconState();
            Host?.ShowToast(target ? "桌面图标已显示" : "桌面图标已隐藏，关闭程序后会自动恢复");
        }

        // ===== 热键开关 =====

        private void HotkeyToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var want = HotkeyToggle.IsChecked == true;
            AppCore.Config.HotkeyEnabled = want;
            AppCore.Save();

            if (want)
            {
                var error = string.Empty;
                if (Host != null && Host.TryRegisterHotkey(out error))
                {
                    Host.ShowToast("快捷键已启用：" + AppCore.Config.Hotkey);
                }
                else
                {
                    AppCore.Config.HotkeyEnabled = false;
                    AppCore.Save();
                    _suppress = true;
                    HotkeyToggle.IsChecked = false;
                    _suppress = false;
                    Host?.ShowToast(string.IsNullOrEmpty(error) ? "快捷键注册失败" : error, false);
                }
            }
            else
            {
                AppCore.Hotkey.UnregisterHotKey();
                Host?.ShowToast("快捷键已关闭");
            }

            RefreshState();
        }

        // ===== 录制新快捷键 =====

        private void HotkeyEditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_recording)
            {
                StopRecording();
                return;
            }

            StartRecording();
        }

        private void StartRecording()
        {
            _recording = true;
            HotkeyChipText.Text = "请按键…";
            HotkeyChip.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            HotkeyEditBtn.Content = "取消";
            HotkeyHint.Text = "可按下组合键，也可直接按单个按键（F7、右 Alt 等）；Esc 取消。";

            // 录制期间先注销旧热键，避免按下旧组合键时误触发
            AppCore.Hotkey.UnregisterHotKey();

            // 用键盘钩子录制：右 Alt / 左 Ctrl 这类键在窗口事件里常常收不到
            AppCore.Keys.Captured -= OnKeysCaptured;
            AppCore.Keys.Captured += OnKeysCaptured;
            AppCore.Keys.CaptureCancelled -= OnCaptureCancelled;
            AppCore.Keys.CaptureCancelled += OnCaptureCancelled;
            AppCore.Keys.StartCapture();

            if (_window != null)
            {
                _window.Deactivated -= OnWindowDeactivated;
                _window.Deactivated += OnWindowDeactivated;
            }
        }

        private void StopRecording()
        {
            if (!_recording) return;

            _recording = false;
            AppCore.Keys.Captured -= OnKeysCaptured;
            AppCore.Keys.CaptureCancelled -= OnCaptureCancelled;
            AppCore.Keys.StopCapture();

            if (_window != null)
            {
                _window.Deactivated -= OnWindowDeactivated;
            }

            HotkeyChip.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
            HotkeyEditBtn.Content = "更改";

            if (AppCore.Config.HotkeyEnabled)
            {
                Host?.TryRegisterHotkey(out _);
            }

            RefreshState();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            StopRecording();
        }

        private void OnKeysCaptured(string text)
        {
            StopRecording();
            ApplyHotkey(text);
        }

        private void OnCaptureCancelled()
        {
            StopRecording();
            Host?.ShowToast("已取消快捷键设置");
        }

        private void ApplyHotkey(string text)
        {
            var old = AppCore.Config.Hotkey;
            AppCore.Config.Hotkey = text;
            AppCore.Save();

            if (!AppCore.Config.HotkeyEnabled)
            {
                // 快捷键总开关关闭时只保存按键，不注册
                Host?.ShowToast("已保存为 " + HotkeyDefinition.Friendly(text) + "，打开上方开关后生效");
                RefreshState();
                return;
            }

            var error = string.Empty;
            if (Host != null && Host.TryRegisterHotkey(out error))
            {
                if (HotkeyDefinition.IsBareKey(text) && IsPlainTypingKey(text))
                {
                    Host.ShowToast(HotkeyDefinition.Friendly(text) + " 是单键，会被全局占用，建议改用 F1–F12", false);
                }
                else
                {
                    Host.ShowToast("快捷键已更新为 " + HotkeyDefinition.Friendly(text));
                }
            }
            else
            {
                AppCore.Config.Hotkey = old;
                AppCore.Save();
                Host?.TryRegisterHotkey(out _);
                Host?.ShowToast(string.IsNullOrEmpty(error) ? "快捷键注册失败" : error, false);
            }

            RefreshState();
        }

        /// <summary>字母 / 数字单键：会抢占日常输入，需要额外提醒。</summary>
        private static bool IsPlainTypingKey(string text)
        {
            return text.Length == 1 && char.IsLetterOrDigit(text[0]);
        }

        // ===== 动作选择 =====

        private void Action_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var action = sender == ActionShowDesktop ? HotkeyAction.ShowDesktop : HotkeyAction.ToggleDesktopIcons;
            AppCore.Config.HotkeyAction = action;
            AppCore.Save();
            Host?.ShowToast(action == HotkeyAction.ShowDesktop
                ? "快捷键将直接显示桌面（Win+D）"
                : "快捷键将隐藏 / 显示桌面图标");
        }

        // ===== 悬浮提示 =====

        private void OverlayToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var want = OverlayToggle.IsChecked == true;
            AppCore.Config.HotkeyOverlay = want;
            AppCore.Save();

            if (want) OsdService.Preview();
            Host?.ShowToast(want ? "按下快捷键时会显示悬浮提示" : "已关闭悬浮提示");
        }

        private void OverlayPreview_Click(object sender, RoutedEventArgs e)
        {
            OsdService.Preview();
        }

        // ===== 常规 =====

        private void TrayToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            AppCore.Config.CloseToTray = TrayToggle.IsChecked == true;
            AppCore.Save();
            Host?.ShowToast(AppCore.Config.CloseToTray
                ? "关闭窗口后将最小化到托盘"
                : "关闭窗口后将直接退出程序");
        }

        private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var want = AutoStartToggle.IsChecked == true;
            AutoStartService.SetEnabled(want);
            AppCore.Config.AutoStart = AutoStartService.IsEnabled;

            _suppress = true;
            AutoStartToggle.IsChecked = AppCore.Config.AutoStart;
            _suppress = false;

            Host?.ShowToast(want ? "已设置为开机自动启动" : "已取消开机自动启动");
        }

        // ===== 资源管理器 =====

        private async void RestartBtn2_Click(object sender, RoutedEventArgs e)
        {
            if (Host == null) return;
            RestartBtn2.IsEnabled = false;
            try
            {
                await Host.RestartExplorerAsync();
            }
            finally
            {
                RestartBtn2.IsEnabled = true;
            }
        }

        private async void DeepRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Host == null) return;
            DeepRefreshBtn.IsEnabled = false;
            try
            {
                await Host.DeepRefreshAsync();
            }
            finally
            {
                DeepRefreshBtn.IsEnabled = true;
            }
        }
    }
}
