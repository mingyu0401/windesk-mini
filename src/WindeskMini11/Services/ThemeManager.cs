using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WindeskMini11.Services
{
    /// <summary>主题切换（浅色 / 深色 / 跟随系统）与标题栏配色。</summary>
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; } = true;

        public static event Action<bool>? Changed;

        public static bool IsSystemDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int i) return i == 0;
            }
            catch (Exception ex)
            {
                Log.Write("读取系统主题失败: " + ex.Message);
            }

            return true;
        }

        public static void Apply(string mode)
        {
            IsDark = mode switch
            {
                "Dark" => true,
                "Light" => false,
                _ => IsSystemDark()
            };

            var app = Application.Current;
            if (app == null) return;

            var dict = new ResourceDictionary
            {
                Source = new Uri("/WindeskMini11;component/Themes/" + (IsDark ? "Dark" : "Light") + ".xaml",
                    UriKind.Relative)
            };

            var merged = app.Resources.MergedDictionaries;
            var replaced = false;
            for (var i = 0; i < merged.Count; i++)
            {
                var src = merged[i].Source?.OriginalString ?? string.Empty;
                if (src.Contains("Themes/Dark.xaml") || src.Contains("Themes/Light.xaml"))
                {
                    merged[i] = dict;
                    replaced = true;
                    break;
                }
            }

            if (!replaced) merged.Insert(0, dict);

            Log.Write("应用主题: " + (IsDark ? "Dark" : "Light") + "（模式 " + mode + "）");
            Changed?.Invoke(IsDark);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        public static void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                var dark = IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

                if (window.TryFindResource("TitleBarColor") is Color caption)
                {
                    var captionRef = caption.R | (caption.G << 8) | (caption.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionRef, sizeof(int));
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref captionRef, sizeof(int));
                }

                if (window.TryFindResource("TitleBarTextColor") is Color text)
                {
                    var textRef = text.R | (text.G << 8) | (text.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textRef, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                Log.Write("设置标题栏失败: " + ex.Message);
            }
        }
    }
}
