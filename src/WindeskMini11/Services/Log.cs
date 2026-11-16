using System;
using System.IO;
using System.Text;

namespace WindeskMini11.Services
{
    /// <summary>轻量文件日志，便于排查系统级修改问题。</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindeskMini11");

        public static string FilePath => Path.Combine(Dir, "app.log");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                lock (Gate)
                {
                    File.AppendAllText(FilePath,
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine,
                        Utf8Bom);
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
