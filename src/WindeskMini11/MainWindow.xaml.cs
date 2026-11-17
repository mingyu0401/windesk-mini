using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindeskMini11.Services;
using WindeskMini11.Views;

namespace WindeskMini11
{
    public partial class MainWindow : Window, IPageHost
    {
        private readonly PersonalizePage _personalizePage = new PersonalizePage();
        private readonly DesktopPage _desktopPage = new DesktopPage();
        private readonly AboutPage _aboutPage = new AboutPage();
        private readonly DispatcherTimer _toastTimer;
        private readonly DispatcherTimer _healthTimer;
        private HwndSource? _sessionSource;
        private bool _reallyExit;

        // 睡眠唤醒 / 会话切换后，系统的低级键盘钩子可能被丢掉，靠这两个消息补救
        private const int WM_POWERBROADCAST = 0x0218;
        private const int WM_WTSSESSION_CHANGE = 0x02B1;
        private const int PBT_APMRESUMESUSPEND = 0x0007;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int WTS_CONSOLE_CONNECT = 0x0001;
        private const int WTS_SESSION_UNLOCK = 0x0008;
        private const int NOTIFY_FOR_THIS_SESSION = 0;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

        public MainWindow()
        {
            InitializeComponent();
            ApplyWindowIcon();

            PageHost.Content = _personalizePage;
            VersionText.Text = "版本 " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

            AdminText.Text = AdminHelper.IsElevated()
                ? "管理员权限已启用"
                : "普通权限运行";
            AdminDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                AdminHelper.IsElevated() ? "SuccessBrush" : "DangerBrush");

            ThemeManager.Changed += OnThemeChanged;
            ThemeManager.ApplyTitleBar(this);

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                HideToast();
            };

            // 兜底自检：钩子该在却不在时补装
            _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _healthTimer.Tick += (_, _) => AppCore.Keys.HealthCheck();
            _healthTimer.Start();

            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;
        }

        /// <summary>
        /// 标题栏 / 任务栏图标直接取 ico 里最大的一帧（256）。
        /// 不设 Icon 时系统会拿 16×16 那一帧，在 150% 缩放下放大到 24 就发虚。
        /// </summary>
        private void ApplyWindowIcon()
        {
            try
            {
                var decoder = BitmapDecoder.Create(
                    new Uri("pack://application:,,,/Assets/app.ico"),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);

                BitmapFrame? best = null;
                foreach (var frame in decoder.Frames)
                {
                    if (best == null || frame.PixelWidth > best.PixelWidth) best = frame;
                }

                if (best != null)
                {
                    Icon = best;
                    Log.Write("窗口图标: 使用 " + best.PixelWidth + "x" + best.PixelHeight + " 帧");
                }
            }
            catch (Exception ex)
            {
                Log.Write("设置窗口图标失败: " + ex.Message);
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            AppCore.Hotkey.Attach(this);
            AppCore.Hotkey.Pressed -= OnHotkeyPressed;
            AppCore.Hotkey.Pressed += OnHotkeyPressed;
            RegisterHotkeyInternal();
            ThemeManager.ApplyTitleBar(this);

            var handle = new WindowInteropHelper(this).Handle;
            _sessionSource = HwndSource.FromHwnd(handle);
            _sessionSource?.AddHook(SessionProc);

            if (WTSRegisterSessionNotification(handle, NOTIFY_FOR_THIS_SESSION))
            {
                Log.Write("已订阅会话通知（锁屏 / 切换用户）");
            }
            else
            {
                Log.Write("订阅会话通知失败: " + Marshal.GetLastWin32Error());
            }
        }

        /// <summary>睡眠唤醒、解锁后重新装一次按键监听。</summary>
        private IntPtr SessionProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_POWERBROADCAST)
            {
                var evt = wParam.ToInt32();
                if (evt == PBT_APMRESUMEAUTOMATIC || evt == PBT_APMRESUMESUSPEND)
                {
                    Log.Write("系统从睡眠恢复，重装按键监听");
                    ReapplyHotkey();
                }
            }
            else if (msg == WM_WTSSESSION_CHANGE)
            {
                var evt = wParam.ToInt32();
                if (evt == WTS_SESSION_UNLOCK || evt == WTS_CONSOLE_CONNECT)
                {
                    Log.Write("会话解锁，重装按键监听");
                    ReapplyHotkey();
                }
            }

            return IntPtr.Zero;
        }

        private void ReapplyHotkey()
        {
            if (!AppCore.Config.HotkeyEnabled) return;

            if (HotkeyDefinition.TryParse(AppCore.Config.Hotkey, out _, out var vk) &&
                HotkeyDefinition.IsModifierOnly(vk))
            {
                AppCore.Keys.ReinstallHook();
            }
            else
            {
                // 组合键走 RegisterHotKey，唤醒后重注册一次更稳妥
                AppCore.Hotkey.Register(AppCore.Config.Hotkey);
            }
        }

        private void RegisterHotkeyInternal()
        {
            if (AppCore.Config.HotkeyEnabled)
            {
                AppCore.Hotkey.Register(AppCore.Config.Hotkey);
            }
            else
            {
                AppCore.Hotkey.UnregisterHotKey();
            }
        }

        private void OnHotkeyPressed()
        {
            // 单修饰键热键由键盘钩子回调触发，统一回到 UI 线程再动作与提示
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnHotkeyPressed));
                return;
            }

            Log.Write("热键触发");
            AppCore.RunHotkeyAction();

            if (AppCore.Config.HotkeyAction == HotkeyAction.ShowDesktop)
            {
                OsdService.ShowDesktop();
            }
            else
            {
                OsdService.ShowIconState(DesktopService.IconsVisible);
            }

            // 悬浮提示关闭时，窗口在前台就用窗口内提示兜底
            if (!AppCore.Config.HotkeyOverlay && IsVisible && WindowState != WindowState.Minimized)
            {
                ShowToast(AppCore.Config.HotkeyAction == HotkeyAction.ShowDesktop
                    ? "已显示桌面"
                    : DesktopService.IconsVisible ? "桌面图标已显示" : "桌面图标已隐藏");
            }
        }

        // ===== 导航 =====

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || PageHost == null) return;

            var tag = (sender as RadioButton)?.Tag as string ?? "personalize";
            NavigateTo(tag);
        }

        private void NavigateTo(string tag)
        {
            var page = tag switch
            {
                "desktop" => (UserControl)_desktopPage,
                "about" => (UserControl)_aboutPage,
                _ => (UserControl)_personalizePage
            };

            if (ReferenceEquals(PageHost.Content, page))
            {
                (page as IRefreshablePage)?.RefreshState();
                return;
            }

            PageHost.Content = page;
            (page as IRefreshablePage)?.RefreshState();

            page.Opacity = 0;
            var transform = new TranslateTransform(0, 12);
            page.RenderTransform = transform;

            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        // ===== IPageHost =====

        public void ShowToast(string message, bool success = true)
        {
            ToastText.Text = message;
            ToastGlyph.Text = success ? "\uE73E" : "\uE783";
            ToastGlyph.SetResourceReference(TextBlock.ForegroundProperty, success ? "SuccessBrush" : "DangerBrush");

            Toast.Visibility = Visibility.Visible;
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            animation.Completed += (_, _) =>
            {
                if (_toastTimer.IsEnabled == false) Toast.Visibility = Visibility.Collapsed;
            };
            Toast.BeginAnimation(OpacityProperty, animation);
        }

        public bool TryRegisterHotkey(out string error)
        {
            if (!AppCore.Config.HotkeyEnabled)
            {
                error = "快捷键已关闭";
                return false;
            }

            var ok = AppCore.Hotkey.Register(AppCore.Config.Hotkey);
            error = ok ? string.Empty : AppCore.Hotkey.LastError;
            return ok;
        }

        public async System.Threading.Tasks.Task RestartExplorerAsync()
        {
            ShowToast("正在重启资源管理器…");
            var ok = await DesktopService.RestartExplorerAsync();
            RefreshAllPages();
            ShowToast(ok ? "资源管理器已重启，图标效果已刷新" : "重启失败，请手动重启资源管理器", ok);
        }

        public async System.Threading.Tasks.Task DeepRefreshAsync()
        {
            var confirmed = DialogService.Confirm(this, "深度刷新图标缓存",
                "将结束并重启资源管理器，并删除 %LocalAppData% 下的图标缓存文件（iconcache*.db）。\n缓存会自动重建，但桌面与任务栏会短暂消失，确定继续吗？",
                "继续", "取消");

            if (!confirmed) return;

            ShowToast("正在清理图标缓存…");
            var ok = await DesktopService.ClearIconCacheAsync();
            RefreshAllPages();
            ShowToast(ok ? "图标缓存已清理，资源管理器已重启" : "清理失败，请手动重启资源管理器", ok);
        }

        public void RefreshAllPages()
        {
            _personalizePage.RefreshState();
            _desktopPage.RefreshState();
            _aboutPage.RefreshState();
        }

        // ===== 显隐与退出 =====

        public void StartHidden()
        {
            // 提前创建窗口句柄，保证热键可用
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
            Hide();
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            RefreshAllPages();
        }

        public void ExitApplication()
        {
            _reallyExit = true;
            Close();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_reallyExit && AppCore.Config.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                AppCore.Tray?.ShowBalloon("桌面美化仍在后台运行", "快捷键依然可用，双击托盘图标可重新打开。");
                return;
            }

            Cleanup();
        }

        private void Cleanup()
        {
            try
            {
                AppCore.Hotkey.Pressed -= OnHotkeyPressed;
                AppCore.Hotkey.Dispose();
                _healthTimer.Stop();

                if (_sessionSource != null)
                {
                    WTSUnRegisterSessionNotification(new WindowInteropHelper(this).Handle);
                    _sessionSource.RemoveHook(SessionProc);
                    _sessionSource = null;
                }

                OsdService.Close();
                DesktopService.EnsureIconsVisible();
                AppCore.Tray?.Dispose();
                AppCore.Tray = null;
                ThemeManager.Changed -= OnThemeChanged;
                Log.Write("=== 退出，已恢复桌面图标显示 ===");
            }
            catch (Exception ex)
            {
                Log.Write("退出清理失败: " + ex.Message);
            }
        }

        private void OnThemeChanged(bool dark)
        {
            ThemeManager.ApplyTitleBar(this);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F5) RefreshAllPages();
        }
    }
}
