using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WindeskMini11.Services;

namespace WindeskMini11.Views
{
    public partial class AboutPage : UserControl, IRefreshablePage
    {
        private bool _checking;
        private bool _autoChecked;
        private string _releaseUrl = UpdateService.ReleasePageUrl;

        public AboutPage()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshState();

            var version = typeof(AboutPage).Assembly.GetName().Version;
            VersionBadge.Text = version == null ? "v1.0.0" : "v" + version.ToString(3);
            UpdateStatus.Text = "当前版本 v" + UpdateService.CurrentVersion + "，点右侧按钮检查是否有新版本。";
        }

        private IPageHost? Host => Window.GetWindow(this) as IPageHost;

        public void RefreshState()
        {
            // 第一次进入本页时静默查一次，失败也不打扰
            if (_autoChecked || !IsLoaded) return;

            _autoChecked = true;
            _ = CheckUpdatesAsync(false);
        }

        // ===== 检查更新 =====

        private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            await CheckUpdatesAsync(true);
        }

        private async Task CheckUpdatesAsync(bool manual)
        {
            if (_checking) return;

            _checking = true;
            CheckUpdateBtn.IsEnabled = false;
            UpdateStatus.Text = "正在检查…";

            try
            {
                var info = await UpdateService.CheckAsync();

                if (!info.Succeeded)
                {
                    UpdateStatus.Text = "检查失败：" + info.Error;
                    if (manual) Host?.ShowToast("检查更新失败：" + info.Error, false);
                    return;
                }

                if (!string.IsNullOrEmpty(info.ReleaseUrl))
                {
                    _releaseUrl = info.ReleaseUrl;
                }

                if (info.IsNewer)
                {
                    UpdateStatus.Text = "发现新版本 " + info.LatestVersion +
                                        "（当前 " + UpdateService.CurrentVersion + "）";
                    OpenReleaseBtn.Visibility = Visibility.Visible;
                    if (manual) Host?.ShowToast("有新版本 " + info.LatestVersion + " 可用");
                }
                else if (info.IsCurrentNewer)
                {
                    // 本地开发版/未发布版本高于 GitHub Release，不应误报为“已是最新”。
                    UpdateStatus.Text = "当前版本 " + UpdateService.CurrentVersion +
                                        " 高于公开版 " + info.LatestVersion + "（本地开发版）";
                    OpenReleaseBtn.Visibility = Visibility.Visible;
                    if (manual) Host?.ShowToast("当前是未发布的较新版本");
                }
                else
                {
                    UpdateStatus.Text = "已是最新版本（" + UpdateService.CurrentVersion + "）";
                    OpenReleaseBtn.Visibility = Visibility.Collapsed;
                    if (manual) Host?.ShowToast("已是最新版本");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus.Text = "检查失败：" + ex.Message;
                Log.Write("检查更新异常: " + ex.Message);
            }
            finally
            {
                _checking = false;
                CheckUpdateBtn.IsEnabled = true;
            }
        }

        private void OpenReleaseBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("打开发布页失败: " + ex.Message);
                Host?.ShowToast("打开浏览器失败", false);
            }
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            if (owner == null) return;

            var confirm = MessageBox.Show(owner,
                "将移除小箭头、小盾牌的系统级覆盖，并把快捷键与主题恢复为默认值。\n是否继续？",
                "恢复默认设置", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK) return;

            try
            {
                ShellIconService.SetArrowRemoved(false);
                ShellIconService.SetShieldRemoved(false);

                AppCore.Config.RemoveShortcutArrow = false;
                AppCore.Config.RemoveShieldOverlay = false;
                AppCore.Config.Hotkey = "Ctrl+Alt+D";
                AppCore.Config.HotkeyAction = HotkeyAction.ToggleDesktopIcons;
                AppCore.Config.HotkeyEnabled = true;
                AppCore.Config.Theme = "System";
                AppCore.Save();

                ThemeManager.Apply("System");
                Host?.TryRegisterHotkey(out _);
                Host?.RefreshAllPages();
                Host?.ShowToast("已恢复默认设置，重启资源管理器后生效");
            }
            catch (Exception ex)
            {
                Log.Write("恢复默认失败: " + ex.Message);
                MessageBox.Show(owner, "恢复失败：" + ex.Message, "桌面美化",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.ExitApplication();
        }
    }
}
