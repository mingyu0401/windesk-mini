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

    public static class AutoStartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WindeskMini11";

        public static bool IsEnabled
        {
            get
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
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enabled)
                {
                    var exe = Environment.ProcessPath ?? string.Empty;
                    key.SetValue(ValueName, "\"" + exe + "\" --tray", Microsoft.Win32.RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write("设置开机启动失败: " + ex.Message);
            }
        }
    }
}
