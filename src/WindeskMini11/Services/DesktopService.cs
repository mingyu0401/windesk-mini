using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WindeskMini11.Services
{
    /// <summary>桌面图标显示状态控制、资源管理器重启、显示桌面。</summary>
    public static class DesktopService
    {
        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        /// <summary>定位桌面的 SysListView32（图标容器）。</summary>
        public static IntPtr FindDesktopListView()
        {
            var progman = FindWindow("Progman", null);
            var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero)
            {
                // 开了壁纸幻灯片时，DefView 会挂在某个 WorkerW 下
                var found = IntPtr.Zero;
                EnumWindows((hWnd, _) =>
                {
                    var dv = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero)
                    {
                        found = dv;
                        return false;
                    }

                    return true;
                }, IntPtr.Zero);
                defView = found;
            }

            if (defView == IntPtr.Zero) return IntPtr.Zero;

            var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            if (listView == IntPtr.Zero)
                listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);

            return listView;
        }

        public static bool IconsVisible
        {
            get
            {
                var listView = FindDesktopListView();
                if (listView == IntPtr.Zero) return true; // 拿不到句柄时按“可见”处理
                return IsWindowVisible(listView);
            }
        }

        public static bool SetIconsVisible(bool visible)
        {
            var listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                Log.Write("未找到桌面图标容器");
                return false;
            }

            ShowWindow(listView, visible ? SW_SHOW : SW_HIDE);
            Log.Write("桌面图标 -> " + (visible ? "显示" : "隐藏"));
            return true;
        }

        public static bool ToggleIcons()
        {
            var target = !IconsVisible;
            SetIconsVisible(target);
            return target;
        }

        /// <summary>确保桌面图标处于显示状态（退出时兜底）。</summary>
        public static void EnsureIconsVisible()
        {
            try
            {
                if (!IconsVisible) SetIconsVisible(true);
            }
            catch (Exception ex)
            {
                Log.Write("恢复桌面图标失败: " + ex.Message);
            }
        }

        /// <summary>模拟 Win + D 显示桌面。</summary>
        public static void ShowDesktop()
        {
            const byte VK_LWIN = 0x5B;
            const byte VK_D = 0x44;
            const uint KEYEVENTF_KEYUP = 0x0002;

            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static async Task<bool> RestartExplorerAsync()
        {
            var wasHidden = !IconsVisible;
            try
            {
                KillExplorer();
                await Task.Delay(600);
                await EnsureExplorerRunningAsync();

                await WaitForDesktopAsync(wasHidden);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("重启资源管理器失败: " + ex);
                return false;
            }
        }

        public static async Task<bool> ClearIconCacheAsync()
        {
            var wasHidden = !IconsVisible;
            try
            {
                KillExplorer();
                await Task.Delay(900);

                foreach (var file in CollectIconCacheFiles())
                {
                    try
                    {
                        File.Delete(file);
                        Log.Write("已删除图标缓存: " + file);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("删除图标缓存失败 " + file + " : " + ex.Message);
                    }
                }

                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    await EnsureExplorerRunningAsync();
                }

                await WaitForDesktopAsync(wasHidden);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("清理图标缓存失败: " + ex);
                return false;
            }
        }

        private static IEnumerable<string> CollectIconCacheFiles()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var list = new List<string>();

            var legacy = Path.Combine(localAppData, "IconCache.db");
            if (File.Exists(legacy)) list.Add(legacy);

            var cacheDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
            if (Directory.Exists(cacheDir))
            {
                foreach (var f in Directory.GetFiles(cacheDir, "iconcache*.db"))
                    list.Add(f);
            }

            return list;
        }

        private static void KillExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    p.Kill();
                }
                catch (Exception ex)
                {
                    Log.Write("结束 explorer 失败: " + ex.Message);
                }
            }
        }

        private static async Task<bool> EnsureExplorerRunningAsync()
        {
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(200);
                if (Process.GetProcessesByName("explorer").Length > 0) return true;
            }

            try
            {
                Log.Write("资源管理器未自动重启，手动启动");
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("启动资源管理器失败: " + ex.Message);
            }

            return false;
        }

        private static async Task WaitForDesktopAsync(bool restoreHidden)
        {
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (FindDesktopListView() != IntPtr.Zero) break;
            }

            if (!restoreHidden) return;

            await Task.Delay(500);
            SetIconsVisible(false);
        }

        public static bool ActivateWindowByTitle(string title)
        {
            var hwnd = FindWindow(null, title);
            if (hwnd == IntPtr.Zero) return false;

            ShowWindow(hwnd, SW_SHOW);
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            return true;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
