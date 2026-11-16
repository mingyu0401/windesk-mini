using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WindeskMini11.Services;

namespace WindeskMini11
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private bool _ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = e.Args ?? Array.Empty<string>();
            var noElevate = args.Any(a => a.Equals("--no-elevate", StringComparison.OrdinalIgnoreCase));
            var startInTray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            // 诊断入口：校验生成的 1bpp 透明图标（不显示任何窗口）
            if (args.Any(a => a.Equals("--diag-icon", StringComparison.OrdinalIgnoreCase)))
            {
                RunIconDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：校验热键解析（含单键）
            if (args.Any(a => a.Equals("--diag-hotkey", StringComparison.OrdinalIgnoreCase)))
            {
                RunHotkeyDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：真实注册热键（含单键），不需要显示窗口
            if (args.Any(a => a.Equals("--diag-hotkey-register", StringComparison.OrdinalIgnoreCase)))
            {
                RunHotkeyRegisterDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：离屏渲染 "个性化" 页面，检查排版（不显示窗口）
            if (args.Any(a => a.Equals("--diag-shot", StringComparison.OrdinalIgnoreCase)))
            {
                AppCore.Init();
                ThemeManager.Apply(AppCore.Config.Theme);
                RunLayoutDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：实机走一遍悬浮提示的显示→停留→淡出流程（窗口在屏幕外）
            if (args.Any(a => a.Equals("--diag-osd-live", StringComparison.OrdinalIgnoreCase)))
            {
                AppCore.Init();
                ThemeManager.Apply(AppCore.Config.Theme);
                RunOsdLiveDiagnostics();
                return;
            }

            // 诊断入口：键盘钩子录制（12 秒），验证右 Alt / 单键能否被捕获
            if (args.Any(a => a.Equals("--diag-hook", StringComparison.OrdinalIgnoreCase)))
            {
                RunHookCaptureDiagnostics();
                return;
            }

            // 诊断入口：检查更新（走真实 GitHub API）
            if (args.Any(a => a.Equals("--diag-update", StringComparison.OrdinalIgnoreCase)))
            {
                RunUpdateDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：开机自启的创建 / 查询 / 删除（会自行清理干净）
            if (args.Any(a => a.Equals("--diag-autostart", StringComparison.OrdinalIgnoreCase)))
            {
                RunAutoStartDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：离屏渲染悬浮提示（三个状态），检查排版与定位计算
            if (args.Any(a => a.Equals("--diag-osd", StringComparison.OrdinalIgnoreCase)))
            {
                AppCore.Init();
                ThemeManager.Apply(AppCore.Config.Theme);
                RunOsdDiagnostics();
                Shutdown();
                return;
            }

            // 诊断入口：键盘钩子监听单个修饰键（12 秒）
            if (args.Any(a => a.Equals("--diag-hook-watch", StringComparison.OrdinalIgnoreCase)))
            {
                RunHookWatchDiagnostics();
                return;
            }

            _mutex = new Mutex(true, @"Local\WindeskMini11.SingleInstance", out _ownsMutex);
            if (!_ownsMutex)
            {
                if (!DesktopService.ActivateWindowByTitle("桌面美化 · Windesk Mini"))
                {
                    MessageBox.Show("桌面美化已在运行，请从系统托盘中打开。", "桌面美化",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            if (!AdminHelper.IsElevated() && !noElevate)
            {
                var relaunchArgs = args
                    .Where(a => !a.Equals("--no-elevate", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (AdminHelper.RelaunchElevated(relaunchArgs))
                {
                    Shutdown();
                    return;
                }

                MessageBox.Show("本程序需要管理员权限才能写入系统注册表。\n请右键选择「以管理员身份运行」。",
                    "桌面美化", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;
            Init(startInTray);
        }

        private void Init(bool startInTray)
        {
            try
            {
                Log.Write("=== 启动 ===" + (AdminHelper.IsElevated() ? " 管理员" : " 普通用户") +
                          " 参数: " + string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));

                AppCore.Init();
                ThemeManager.Apply(AppCore.Config.Theme);

                // 兜底：上次若异常退出导致桌面图标被隐藏，这里恢复显示
                DesktopService.EnsureIconsVisible();

                var tray = new TrayService();
                AppCore.Tray = tray;

                var window = new MainWindow();
                AppCore.Window = window;
                MainWindow = window;

                tray.OpenRequested += () => Dispatcher.Invoke(() => window.ShowFromTray());
                tray.ExitRequested += () => Dispatcher.Invoke(() => window.ExitApplication());
                tray.ToggleDesktopRequested += () => Dispatcher.Invoke(() => AppCore.RunHotkeyAction());
                tray.RestartExplorerRequested += () => Dispatcher.Invoke(async () =>
                {
                    await window.RestartExplorerAsync();
                });

                if (startInTray)
                {
                    window.StartHidden();
                }
                else
                {
                    window.Show();
                }
            }
            catch (Exception ex)
            {
                Log.Write("启动失败: " + ex);
                MessageBox.Show("启动失败：" + ex.Message, "桌面美化", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        public void ExitApplication()
        {
            AppCore.Window?.ExitApplication();
        }

        /// <summary>钩子录制自检：12 秒内记录按键组合。</summary>
        private static void RunHookCaptureDiagnostics(int seconds = 12)
        {
            var monitor = new GlobalKeyMonitor();
            monitor.Captured += text =>
            {
                Log.Write("钩子自检: 捕获 " + text + "（界面显示为 " + HotkeyDefinition.Friendly(text) + "）");
                monitor.StartCapture(); // 继续监听下一组
            };
            monitor.CaptureCancelled += () =>
            {
                Log.Write("钩子自检: 被 Esc 取消");
                monitor.StartCapture();
            };

            monitor.StartCapture();
            Log.Write("钩子自检: 开始监听 " + seconds + " 秒，请依次按 右Alt / F7 / Ctrl+Alt+D");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                monitor.StopCapture();
                monitor.Dispose();
                Log.Write("钩子自检: 结束");
                Current.Shutdown();
            };
            timer.Start();
        }

        /// <summary>单修饰键热键自检：监听右 Alt 单击，12 秒。</summary>
        private static void RunHookWatchDiagnostics(int seconds = 12)
        {
            var monitor = new GlobalKeyMonitor();
            monitor.WatchedKeyTriggered += () => Log.Write("钩子自检: 单个修饰键触发 -> 切换桌面图标");
            monitor.WatchModifierKey(0xA5); // VK_RMENU
            Log.Write("钩子自检: 开始监听右 Alt 单击 " + seconds + " 秒");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                monitor.StopWatching();
                monitor.Dispose();
                Log.Write("钩子自检: 结束，钩子剩余状态=" + monitor.IsHooked);
                Current.Shutdown();
            };
            timer.Start();
        }

        /// <summary>校验热键字符串解析（组合键 + 单键），结果写日志。</summary>
        private static void RunHotkeyDiagnostics()
        {
            var samples = new[]
            {
                "F8", "F7", "Ctrl+Alt+D", "RAlt", "LAlt", "RCtrl", "LShift", "RWin",
                "D", "5", "Left", "Num5", "Ctrl+Shift+F9", "+", "Ctrl+", ""
            };
            foreach (var text in samples)
            {
                var ok = HotkeyDefinition.TryParse(text, out var mods, out var vk);
                Log.Write("热键自检: '" + text + "' => 解析=" + ok +
                          " mods=0x" + mods.ToString("X") + " vk=0x" + vk.ToString("X") +
                          " 归一化=" + (ok ? HotkeyDefinition.Describe(mods, vk) : "-"));
            }

            Log.Write("热键自检: 完成");
        }

        /// <summary>用隐藏窗口真实注册热键（含单键），验证系统是否接受。</summary>
        private static void RunHotkeyRegisterDiagnostics()
        {
            HwndSource? source = null;
            try
            {
                source = new HwndSource(new HwndSourceParameters("WindeskHotkeyDiag")
                {
                    Width = 1,
                    Height = 1,
                    WindowStyle = 0
                });

                using var manager = new HotkeyManager();
                manager.Attach(source.Handle);

                foreach (var combo in new[] { "F8", "F9", "Ctrl+Alt+F12", "D" })
                {
                    var ok = manager.Register(combo);
                    Log.Write("注册自检: " + combo + " => " + (ok ? "成功" : "失败 " + manager.LastError));
                    manager.UnregisterHotKey();
                }
            }
            catch (Exception ex)
            {
                Log.Write("注册自检失败: " + ex);
            }
            finally
            {
                source?.Dispose();
            }
        }

        /// <summary>离屏渲染页面并截图，用于无窗口环境下检查排版。</summary>
        private static void RunLayoutDiagnostics()
        {
            try
            {
                var dir = System.IO.Path.Combine(AppConfig.Dir, "diag");
                System.IO.Directory.CreateDirectory(dir);

                foreach (var pair in new[]
                {
                    ("personalize", (System.Windows.FrameworkElement)new Views.PersonalizePage()),
                    ("desktop", new Views.DesktopPage()),
                    ("about", new Views.AboutPage())
                })
                {
                    var container = new System.Windows.Controls.Grid
                    {
                        Width = 808,
                        Height = 900,
                        Background = Application.Current.TryFindResource("WindowBgBrush") as System.Windows.Media.Brush
                    };

                    System.Windows.Documents.TextElement.SetFontFamily(container,
                        new System.Windows.Media.FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"));
                    System.Windows.Documents.TextElement.SetForeground(container,
                        Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush);

                    container.Children.Add(pair.Item2);

                    (pair.Item2 as IRefreshablePage)?.RefreshState();

                    container.Measure(new System.Windows.Size(808, 900));
                    container.Arrange(new System.Windows.Rect(0, 0, 808, 900));
                    container.UpdateLayout();

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(808, 900, 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(container);

                    var path = System.IO.Path.Combine(dir, "page-" + pair.Item1 + ".png");
                    using var stream = System.IO.File.Create(path);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    encoder.Save(stream);

                    Log.Write("排版自检: " + path);
                }
            }
            catch (Exception ex)
            {
                Log.Write("排版自检失败: " + ex);
            }
        }

        /// <summary>检查更新自检：请求 GitHub Releases 并解析版本。</summary>
        private static void RunUpdateDiagnostics()
        {
            try
            {
                Log.Write("更新自检: 当前版本=" + UpdateService.CurrentVersion);

                var info = UpdateService.CheckAsync().GetAwaiter().GetResult();

                if (info.Succeeded)
                {
                    Log.Write("更新自检: 最新=" + info.LatestVersion +
                              " 有新版=" + info.IsNewer +
                              " 链接=" + info.ReleaseUrl);
                }
                else
                {
                    Log.Write("更新自检: 失败 " + info.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Write("更新自检失败: " + ex);
            }
        }

        /// <summary>开机自启自检：开启 → 查看方式 → 关闭，结束时不留残留。</summary>
        private static void RunAutoStartDiagnostics()
        {
            try
            {
                var initial = AutoStartService.Refresh();
                Log.Write("自启自检: 初始启用=" + initial +
                          " 方式=" + AutoStartService.Current +
                          " 管理员=" + AdminHelper.IsElevated());

                var enableOk = AutoStartService.SetEnabled(true, out var enableMessage);
                Log.Write("自启自检: 开启 => " + enableOk + " | " + enableMessage +
                          " | 方式=" + AutoStartService.Current);

                var disableOk = AutoStartService.SetEnabled(false, out var disableMessage);
                Log.Write("自启自检: 关闭 => " + disableOk + " | " + disableMessage +
                          " | 方式=" + AutoStartService.Current);

                Log.Write("自启自检: 完成");
            }
            catch (Exception ex)
            {
                Log.Write("自启自检失败: " + ex);
            }
        }

        /// <summary>实机运行一次悬浮提示（窗口在屏幕外），验证显示与自动隐藏。</summary>
        private static void RunOsdLiveDiagnostics()
        {
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            OsdService.OffscreenMode = true;

            Log.Write("悬浮提示实机自检: 开始");
            OsdService.Preview();

            var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            check.Tick += (_, _) =>
            {
                check.Stop();
                Log.Write("悬浮提示实机自检: 显示中=" + OsdService.IsShowing +
                          " 位置=" + OsdService.Placement +
                          " 样式=" + OsdService.Style);
            };
            check.Start();

            var finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            finish.Tick += (_, _) =>
            {
                finish.Stop();
                Log.Write("悬浮提示实机自检: 3 秒后仍显示=" + OsdService.IsShowing + "（期望 False）");
                OsdService.Close();
                Current.Shutdown();
            };
            finish.Start();
        }

        /// <summary>离屏渲染悬浮提示的三个状态，检查排版与定位换算（不显示在桌面上）。</summary>
        private static void RunOsdDiagnostics()
        {
            // 诊断期间会连续开关多个窗口，避免首个窗口关闭就触发退出
            var previousMode = Current.ShutdownMode;
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                var dir = System.IO.Path.Combine(AppConfig.Dir, "diag");
                System.IO.Directory.CreateDirectory(dir);

                var hotkey = HotkeyDefinition.Friendly(AppCore.Config.Hotkey);

                foreach (var sample in new[]
                {
                    ("hidden", "桌面图标已隐藏", hotkey + " · 再按一次恢复", true),
                    ("shown", "桌面图标已显示", hotkey + " · 再按一次隐藏", false),
                    ("desktop", "已显示桌面", hotkey + " · 再次按下切换窗口", false)
                })
                {
                    var win = new Views.OsdWindow();
                    win.ShowOffscreen(sample.Item2, sample.Item3, sample.Item4);

                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(win);
                    var pw = (int)Math.Ceiling(win.ActualWidth * dpi.DpiScaleX);
                    var ph = (int)Math.Ceiling(win.ActualHeight * dpi.DpiScaleY);

                    if (pw <= 0 || ph <= 0)
                    {
                        Log.Write("悬浮提示自检: " + sample.Item1 + " 布局尺寸异常 " +
                                  win.ActualWidth + "x" + win.ActualHeight +
                                  " dpi=" + dpi.DpiScaleX + "," + dpi.DpiScaleY);
                        win.Close();
                        continue;
                    }

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(pw, ph,
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render((System.Windows.Media.Visual)win.Content);

                    var path = System.IO.Path.Combine(dir, "osd-" + sample.Item1 + ".png");
                    using (var stream = System.IO.File.Create(path))
                    {
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                    }

                    Log.Write("悬浮提示自检: " + path + " 输出 " + pw + "x" + ph +
                              "，定位 " + win.LastPlacement);
                    win.Close();
                }

                Log.Write("悬浮提示自检: 完成");
            }
            catch (Exception ex)
            {
                Log.Write("悬浮提示自检失败: " + ex);
            }
            finally
            {
                Current.ShutdownMode = previousMode;
            }
        }

        /// <summary>把生成的透明图标落盘并做结构与渲染校验，结果写日志。</summary>
        private static void RunIconDiagnostics()
        {
            try
            {
                var dir = System.IO.Path.Combine(AppConfig.Dir, "diag");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "blank-1bpp.ico");

                var bytes = BlankIconFactory.Build();
                System.IO.File.WriteAllBytes(path, bytes);

                var structural = BlankIconFactory.IsBlank1Bpp(bytes);
                Log.Write("图标自检: " + path + " 共 " + bytes.Length + " 字节, 1bpp结构校验=" + structural);

                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                foreach (var frame in decoder.Frames)
                {
                    var bitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        frame,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        0);

                    var stride = bitmap.PixelWidth * 4;
                    var pixels = new byte[stride * bitmap.PixelHeight];
                    bitmap.CopyPixels(pixels, stride, 0);

                    var opaque = 0;
                    for (var i = 3; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] != 0) opaque++;
                    }

                    Log.Write("图标自检: " + bitmap.PixelWidth + "x" + bitmap.PixelHeight +
                              " 解码成功, 不透明像素=" + opaque +
                              " / " + (bitmap.PixelWidth * bitmap.PixelHeight));
                }

                Log.Write("图标自检: 完成");
            }
            catch (Exception ex)
            {
                Log.Write("图标自检失败: " + ex);
            }
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Write("未处理异常: " + e.Exception);
            e.Handled = true;

            var message = e.Exception.GetBaseException().Message;
            if (AppCore.Window != null && AppCore.Window.IsVisible)
            {
                AppCore.Window.ShowToast("出错了：" + message, false);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                DesktopService.EnsureIconsVisible();
                AppCore.Tray?.Dispose();
                if (_ownsMutex) _mutex?.ReleaseMutex();
            }
            catch (Exception ex)
            {
                Log.Write("退出异常: " + ex.Message);
            }

            base.OnExit(e);
        }
    }
}
