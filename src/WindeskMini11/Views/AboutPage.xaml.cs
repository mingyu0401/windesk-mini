using System;
using System.Windows;
using System.Windows.Controls;
using WindeskMini11.Services;

namespace WindeskMini11.Views
{
    public partial class AboutPage : UserControl, IRefreshablePage
    {
        public AboutPage()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshState();

            var version = typeof(AboutPage).Assembly.GetName().Version;
            VersionBadge.Text = version == null ? "v1.0.0" : "v" + version.ToString(3);
        }

        private IPageHost? Host => Window.GetWindow(this) as IPageHost;

        public void RefreshState()
        {
            // 目前无需动态刷新的内容
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
