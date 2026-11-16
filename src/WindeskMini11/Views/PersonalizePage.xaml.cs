using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WindeskMini11.Services;

namespace WindeskMini11.Views
{
    public partial class PersonalizePage : UserControl, IRefreshablePage
    {
        private bool _suppress;

        public PersonalizePage()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshState();
        }

        private IPageHost? Host => Window.GetWindow(this) as IPageHost;

        public void RefreshState()
        {
            _suppress = true;
            try
            {
                var cfg = AppCore.Config;
                ArrowToggle.IsChecked = cfg.RemoveShortcutArrow;
                ShieldToggle.IsChecked = cfg.RemoveShieldOverlay;

                ThemeSystem.IsChecked = cfg.Theme == "System";
                ThemeLight.IsChecked = cfg.Theme == "Light";
                ThemeDark.IsChecked = cfg.Theme == "Dark";

                UpdateBadges();
            }
            finally
            {
                _suppress = false;
            }
        }

        private void UpdateBadges()
        {
            var arrow = ArrowToggle.IsChecked == true;
            var shield = ShieldToggle.IsChecked == true;

            ArrowBadgeText.Text = arrow ? "已开启" : "未启用";
            ShieldBadgeText.Text = shield ? "已开启" : "未启用";

            RestartHint.Visibility = arrow || shield ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ArrowToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var want = ArrowToggle.IsChecked == true;
            try
            {
                ShellIconService.SetArrowRemoved(want);
                AppCore.Config.RemoveShortcutArrow = want;
                AppCore.Save();
                UpdateBadges();
                Host?.ShowToast(want ? "已去除快捷方式小箭头，重启资源管理器后生效" : "已恢复默认的小箭头");
                Log.Write("切换小箭头: " + want);
            }
            catch (Exception ex)
            {
                Log.Write("切换小箭头失败: " + ex.Message);
                _suppress = true;
                ArrowToggle.IsChecked = !want;
                _suppress = false;
                MessageBox.Show(Window.GetWindow(this)!, "修改失败：" + ex.Message, "桌面美化",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShieldToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var want = ShieldToggle.IsChecked == true;
            try
            {
                ShellIconService.SetShieldRemoved(want);
                AppCore.Config.RemoveShieldOverlay = want;
                AppCore.Save();
                UpdateBadges();
                Host?.ShowToast(want ? "已去除小盾牌，重启资源管理器后生效" : "已恢复默认的小盾牌");
                Log.Write("切换小盾牌: " + want);
            }
            catch (Exception ex)
            {
                Log.Write("切换小盾牌失败: " + ex.Message);
                _suppress = true;
                ShieldToggle.IsChecked = !want;
                _suppress = false;
                MessageBox.Show(Window.GetWindow(this)!, "修改失败：" + ex.Message, "桌面美化",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;

            var mode = sender == ThemeLight ? "Light" : sender == ThemeDark ? "Dark" : "System";
            AppCore.Config.Theme = mode;
            AppCore.Save();
            ThemeManager.Apply(mode);
            Host?.ShowToast("已切换到" + (sender as RadioButton)?.Content + "主题");
        }

        private async void RestartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Host == null) return;
            RestartBtn.IsEnabled = false;
            try
            {
                // 盾牌/箭头会被缓存进图标库，必须重建缓存才能真正刷新
                await Host.DeepRefreshAsync();
            }
            finally
            {
                RestartBtn.IsEnabled = true;
            }
        }

        private void LaterBtn_Click(object sender, RoutedEventArgs e)
        {
            RestartHint.Visibility = Visibility.Collapsed;
        }
    }
}
