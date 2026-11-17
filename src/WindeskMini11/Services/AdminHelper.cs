using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace WindeskMini11.Services
{
    public static class AdminHelper
    {
        public static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>以管理员身份重启自身，返回是否成功发起。</summary>
        public static bool RelaunchElevated(IEnumerable<string> args)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = sb.ToString()
                };

                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("提权失败: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>开机自启动的实际实现方式。</summary>
    public enum AutoStartMethod
    {
        None = 0,

        /// <summary>计划任务：登录后以最高权限静默启动，不弹 UAC。</summary>
        ScheduledTask = 1,

        /// <summary>注册表 Run 项：会弹 UAC，属于降级方案。</summary>
        RegistryRun = 2
    }

    /// <summary>
    /// 开机自启动。优先建计划任务（/RL HIGHEST），登录后直接以管理员身份静默运行，
    /// 不会像 HKCU Run 那样每次开机弹 UAC；创建失败才退回 Run 项。
    /// </summary>
    public static class AutoStartService
    {
        private const string TaskName = "WindeskMini11";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WindeskMini11";

        private static string ExePath => Environment.ProcessPath ?? string.Empty;

        /// <summary>缓存的实际方式，避免界面每次刷新都去跑一次 schtasks。</summary>
        public static AutoStartMethod Current { get; private set; } = AutoStartMethod.None;

        public static bool IsEnabled => Current != AutoStartMethod.None;

        public static AutoStartMethod Query()
        {
            if (TaskExists()) return AutoStartMethod.ScheduledTask;
            if (RunValueExists()) return AutoStartMethod.RegistryRun;
            return AutoStartMethod.None;
        }

        /// <summary>重新查询并缓存，返回是否已启用。</summary>
        public static bool Refresh()
        {
            Current = Query();
            return IsEnabled;
        }

        public static bool SetEnabled(bool enabled, out string message)
        {
            message = string.Empty;

            try
            {
                return enabled ? EnableCore(out message) : DisableCore(out message);
            }
            finally
            {
                Current = Query();
            }
        }

        private static bool DisableCore(out string message)
        {
            DeleteTask();
            DeleteRunValue();
            message = "已取消开机自启动";
            Log.Write("开机自启: 已关闭");
            return true;
        }

        private static bool EnableCore(out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(ExePath))
            {
                message = "无法获取程序路径";
                return false;
            }

            if (CreateTask(out var taskError))
            {
                DeleteRunValue(); // 清掉旧写法，避免开两份
                message = "已开启：开机后静默启动到托盘，不再弹 UAC";
                Log.Write("开机自启: 计划任务已创建 -> " + ExePath);
                return true;
            }

            if (WriteRunValue())
            {
                message = "已开启，但只能写入启动项：开机时会弹 UAC 确认";
                Log.Write("开机自启: 计划任务失败(" + taskError + ")，退回 Run 项");
                return true;
            }

            message = "设置失败：" + (string.IsNullOrEmpty(taskError) ? "权限不足" : taskError);
            Log.Write("开机自启: 失败 " + taskError);
            return false;
        }

        /// <summary>
        /// 启动时校正：按当前路径重建任务（程序挪位置也不会失效）；
        /// 老版本只写了 Run 项的，顺手升级成计划任务。
        /// </summary>
        public static void EnsureUpToDate()
        {
            if (Current == AutoStartMethod.None) return;

            try
            {
                if (CreateTask(out _))
                {
                    if (Current == AutoStartMethod.RegistryRun)
                    {
                        Log.Write("开机自启: Run 项已升级为计划任务");
                    }

                    DeleteRunValue();
                    Current = AutoStartMethod.ScheduledTask;
                }
            }
            catch (Exception ex)
            {
                Log.Write("自启校正失败: " + ex.Message);
            }
        }

        // ===== 计划任务 =====

        private static bool TaskExists()
        {
            return RunSchtasks(out _, "/Query", "/TN", TaskName) == 0;
        }

        private static bool CreateTask(out string error)
        {
            error = string.Empty;

            var exe = ExePath;
            if (string.IsNullOrEmpty(exe))
            {
                error = "程序路径未知";
                return false;
            }

            var action = "\"" + exe + "\" --tray";

            // 不带 /DELAY：登录即启动，热键立刻可用。
            // 万一托盘图标因为 explorer 还没起来而添加失败，TaskbarCreated 广播会自动补上。
            var code = RunSchtasks(out var output,
                "/Create",
                "/TN", TaskName,
                "/TR", action,
                "/SC", "ONLOGON",
                "/RL", "HIGHEST",
                "/F");

            if (code == 0) return true;

            error = "schtasks 返回 " + code +
                    (AdminHelper.IsElevated() ? string.Empty : "（缺少管理员权限）");

            if (!string.IsNullOrWhiteSpace(output))
            {
                Log.Write("开机自启: schtasks 输出 " + output.Trim());
            }

            return false;
        }

        private static void DeleteTask()
        {
            if (TaskExists())
            {
                RunSchtasks(out _, "/Delete", "/TN", TaskName, "/F");
            }
        }

        private static int RunSchtasks(out string output, params string[] args)
        {
            output = string.Empty;

            try
            {
                var psi = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                foreach (var arg in args) psi.ArgumentList.Add(arg);

                using var process = Process.Start(psi);
                if (process == null) return -1;

                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(8000);
                return process.HasExited ? process.ExitCode : -1;
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        // ===== 降级方案：HKCU Run =====

        private static bool RunValueExists()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string s && s.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool WriteRunValue()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (key == null) return false;

                key.SetValue(ValueName, "\"" + ExePath + "\" --tray",
                    Microsoft.Win32.RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("写启动项失败: " + ex.Message);
                return false;
            }
        }

        private static void DeleteRunValue()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                key?.DeleteValue(ValueName, false);
            }
            catch (Exception ex)
            {
                Log.Write("删除启动项失败: " + ex.Message);
            }
        }
    }
}
