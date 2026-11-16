using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WindeskMini11.Services
{
    /// <summary>
    /// 通过 HKLM\...\Explorer\Shell Icons 覆盖系统图标槽位，实现“去箭头 / 去盾牌”。
    /// 29 = 快捷方式链接覆盖图标（小箭头），77 = 用户账户控制覆盖图标（小盾牌）。
    /// 两者都指向同一个 1bpp 全透明图标。
    /// </summary>
    public static class ShellIconService
    {
        public const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
        public const string PerUserKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";

        public const string ArrowValueName = "29";
        public const string ShieldValueName = "77";

        private static string? _blankIconPath;

        /// <summary>透明图标的落盘位置（注册表指向它）。</summary>
        public static string BlankIconPath
        {
            get
            {
                if (_blankIconPath != null) return _blankIconPath;

                var preferred = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Blank.ico");

                try
                {
                    EnsureIconFile(preferred);
                    _blankIconPath = preferred;
                }
                catch (Exception ex)
                {
                    Log.Write("写入系统目录失败，改用用户目录: " + ex.Message);
                    var fallback = Path.Combine(AppConfig.Dir, "Blank.ico");
                    Directory.CreateDirectory(AppConfig.Dir);
                    EnsureIconFile(fallback);
                    _blankIconPath = fallback;
                }

                return _blankIconPath;
            }
        }

        /// <summary>确保目标位置存在“1bpp 全透明”图标；旧格式会先备份再替换。</summary>
        private static void EnsureIconFile(string target)
        {
            if (File.Exists(target))
            {
                var existing = File.ReadAllBytes(target);
                if (BlankIconFactory.IsBlank1Bpp(existing))
                {
                    return; // 已是可用格式
                }

                BackupExisting(target);
            }

            var bytes = BlankIconFactory.Build();
            File.WriteAllBytes(target, bytes);

            var check = File.ReadAllBytes(target);
            if (!BlankIconFactory.IsBlank1Bpp(check))
            {
                throw new InvalidOperationException("透明图标写入校验失败: " + target);
            }

            Log.Write("已写入 1bpp 透明图标: " + target + " (" + check.Length + " 字节)");
        }

        private static void BackupExisting(string target)
        {
            try
            {
                var backupDir = Path.Combine(AppConfig.Dir, "backup");
                Directory.CreateDirectory(backupDir);
                var backup = Path.Combine(backupDir, Path.GetFileNameWithoutExtension(target) + ".orig.ico");
                if (!File.Exists(backup))
                {
                    File.Copy(target, backup);
                    Log.Write("已备份原图标: " + backup);
                }
            }
            catch (Exception ex)
            {
                Log.Write("备份原图标失败（继续覆盖）: " + ex.Message);
            }
        }

        /// <summary>
        /// 盾牌槽位使用的透明图标。优先用系统自带的透明图标资源 shell32.dll,50
        /// （实测 16/32/48 三种尺寸均为 0 个不透明像素），不依赖磁盘上的自定义文件。
        /// </summary>
        public static string ShieldIconValue
        {
            get
            {
                var shell32 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "shell32.dll");

                return File.Exists(shell32) ? shell32 + ",50" : BlankIconPath;
            }
        }

        /// <summary>启动时自愈：修正旧版本写入的不可用图标（32bpp 透明图标在叠加槽里不生效）。</summary>
        public static void RepairIconFileIfNeeded()
        {
            try
            {
                var arrowRemoved = IsArrowRemoved();
                var shieldRemoved = IsShieldRemoved();
                if (!arrowRemoved && !shieldRemoved) return;

                EnsureIconFile(BlankIconPath);

                // 旧版本把 77 指向 Blank.ico（32bpp），Windows 不认；迁移到系统透明图标
                var current = ReadValue(ShieldValueName);
                if (shieldRemoved
                    && !string.IsNullOrWhiteSpace(current)
                    && current.EndsWith("Blank.ico", StringComparison.OrdinalIgnoreCase)
                    && !current.Equals(ShieldIconValue, StringComparison.OrdinalIgnoreCase))
                {
                    SetOverlay(ShieldValueName, true, ShieldIconValue);
                    Log.Write("已迁移盾牌槽位: " + current + " -> " + ShieldIconValue);
                }
            }
            catch (Exception ex)
            {
                Log.Write("自愈图标设置失败: " + ex.Message);
            }
        }

        public static bool IsArrowRemoved()
        {
            return !string.IsNullOrWhiteSpace(ReadValue(ArrowValueName));
        }

        public static bool IsShieldRemoved()
        {
            return !string.IsNullOrWhiteSpace(ReadValue(ShieldValueName));
        }

        public static string? ReadValue(string name)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
                return key?.GetValue(name) as string;
            }
            catch (Exception ex)
            {
                Log.Write("读取 Shell Icons 失败: " + ex.Message);
                return null;
            }
        }

        public static void SetArrowRemoved(bool removed)
        {
            SetOverlay(ArrowValueName, removed, BlankIconPath);
        }

        public static void SetShieldRemoved(bool removed)
        {
            SetOverlay(ShieldValueName, removed, ShieldIconValue);
        }

        private static void SetOverlay(string valueName, bool removed, string iconValue)
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath, true)
                            ?? throw new InvalidOperationException("无法写入系统注册表（需要管理员权限）");

            if (removed)
            {
                key.SetValue(valueName, iconValue, RegistryValueKind.String);
                Log.Write("设置 Shell Icons\\" + valueName + " = " + iconValue);
            }
            else
            {
                key.DeleteValue(valueName, false);
                Log.Write("删除 Shell Icons\\" + valueName);
            }

            // 清理用户级同名覆盖，避免与系统级设置冲突
            try
            {
                using var userKey = Registry.CurrentUser.OpenSubKey(PerUserKeyPath, true);
                userKey?.DeleteValue(valueName, false);
            }
            catch (Exception ex)
            {
                Log.Write("清理 HKCU 覆盖失败: " + ex.Message);
            }

            NotifyShell();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;
        private const uint SHCNF_FLUSH = 0x1000;

        public static void NotifyShell()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Write("通知外壳刷新失败: " + ex.Message);
            }
        }
    }
}
