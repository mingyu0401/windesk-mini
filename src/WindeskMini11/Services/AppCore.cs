using System;

namespace WindeskMini11.Services
{
    /// <summary>运行期共享状态。</summary>
    public static class AppCore
    {
        public static AppConfig Config { get; private set; } = new AppConfig();
        public static HotkeyManager Hotkey { get; } = new HotkeyManager();

        /// <summary>全局键盘监听：录制快捷键、以及"单个修饰键"热键用。</summary>
        public static GlobalKeyMonitor Keys { get; } = new GlobalKeyMonitor();

        public static TrayService? Tray { get; set; }
        public static MainWindow? Window { get; set; }

        /// <summary>桌面图标 / 热键等状态变化，供界面刷新。</summary>
        public static event Action? DesktopStateChanged;

        public static void Init()
        {
            Config = AppConfig.Load();
            Hotkey.Monitor = Keys;
            SyncFromRegistry();
        }

        /// <summary>以注册表实际状态为准，避免配置与系统不一致。</summary>
        public static void SyncFromRegistry()
        {
            Config.RemoveShortcutArrow = ShellIconService.IsArrowRemoved();
            Config.RemoveShieldOverlay = ShellIconService.IsShieldRemoved();
            Config.AutoStart = AutoStartService.IsEnabled;

            // 旧版本写入的是 32bpp 透明图标，Windows 不认，这里自愈成 1bpp
            ShellIconService.RepairIconFileIfNeeded();
        }

        public static void Save()
        {
            Config.Save();
        }

        public static void RaiseDesktopStateChanged()
        {
            DesktopStateChanged?.Invoke();
        }

        /// <summary>执行热键绑定的动作。</summary>
        public static void RunHotkeyAction()
        {
            try
            {
                if (Config.HotkeyAction == HotkeyAction.ShowDesktop)
                {
                    DesktopService.ShowDesktop();
                }
                else
                {
                    DesktopService.ToggleIcons();
                }
            }
            catch (Exception ex)
            {
                Log.Write("执行热键动作失败: " + ex.Message);
            }
            finally
            {
                RaiseDesktopStateChanged();
            }
        }
    }
}
