using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindeskMini11.Services
{
    public enum HotkeyAction
    {
        /// <summary>显示 / 隐藏桌面图标</summary>
        ToggleDesktopIcons = 0,

        /// <summary>显示桌面（等同 Win + D）</summary>
        ShowDesktop = 1
    }

    public class AppConfig
    {
        public bool RemoveShortcutArrow { get; set; }
        public bool RemoveShieldOverlay { get; set; }
        public bool HotkeyEnabled { get; set; } = true;
        public string Hotkey { get; set; } = "Ctrl+Alt+D";
        public HotkeyAction HotkeyAction { get; set; } = HotkeyAction.ToggleDesktopIcons;

        /// <summary>按下快捷键时在屏幕下方显示置顶悬浮提示。</summary>
        public bool HotkeyOverlay { get; set; } = true;

        public bool CloseToTray { get; set; } = true;
        public string Theme { get; set; } = "System";
        public bool AutoStart { get; set; }

        [JsonIgnore]
        public static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindeskMini11");

        [JsonIgnore]
        public static string FilePath => Path.Combine(Dir, "settings.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath));
                    if (cfg != null)
                    {
                        if (string.IsNullOrWhiteSpace(cfg.Hotkey)) cfg.Hotkey = "Ctrl+Alt+D";
                        if (string.IsNullOrWhiteSpace(cfg.Theme)) cfg.Theme = "System";
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取配置失败: " + ex.Message);
            }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Log.Write("保存配置失败: " + ex.Message);
            }
        }
    }
}
