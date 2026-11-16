using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WindeskMini11.Services
{
    /// <summary>托盘图标：窗口关闭后热键依然可用。</summary>
    public sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _desktopItem;
        private readonly ToolStripMenuItem _openItem;

        public event Action? OpenRequested;
        public event Action? ExitRequested;
        public event Action? ToggleDesktopRequested;
        public event Action? RestartExplorerRequested;

        public TrayService()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "桌面美化 · Windesk Mini",
                Visible = true
            };

            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                BackColor = Color.FromArgb(43, 43, 43),
                ForeColor = Color.FromArgb(240, 240, 240),
                Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
                Font = new Font("Microsoft YaHei UI", 9f),
                Padding = new Padding(2)
            };

            _openItem = CreateItem("打开主界面", () => OpenRequested?.Invoke());
            _desktopItem = CreateItem("隐藏桌面图标", () => ToggleDesktopRequested?.Invoke());
            var restartItem = CreateItem("重启资源管理器", () => RestartExplorerRequested?.Invoke());
            var exitItem = CreateItem("退出", () => ExitRequested?.Invoke());

            menu.Items.Add(_openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_desktopItem);
            menu.Items.Add(restartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        }

        private static ToolStripMenuItem CreateItem(string text, Action onClick)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = Color.FromArgb(240, 240, 240) };
            item.Click += (_, _) => onClick();
            return item;
        }

        public void UpdateDesktopState(bool iconsVisible)
        {
            _desktopItem.Text = iconsVisible ? "隐藏桌面图标" : "显示桌面图标";
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(2500, title, text, ToolTipIcon.Info);
            }
            catch
            {
                // 忽略
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var icon = Icon.ExtractAssociatedIcon(exe);
                    if (icon != null) return icon;
                }
            }
            catch
            {
                // 忽略
            }

            return SystemIcons.Application;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(43, 43, 43);
            public override Color MenuBorder => Color.FromArgb(60, 60, 60);
            public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
            public override Color MenuItemSelected => Color.FromArgb(62, 62, 62);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(62, 62, 62);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(62, 62, 62);
            public override Color SeparatorDark => Color.FromArgb(58, 58, 58);
            public override Color SeparatorLight => Color.FromArgb(58, 58, 58);
            public override Color ImageMarginGradientBegin => Color.FromArgb(43, 43, 43);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(43, 43, 43);
            public override Color ImageMarginGradientEnd => Color.FromArgb(43, 43, 43);
        }
    }
}
