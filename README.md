# Windesk Mini

Windows 11 桌面图标美化小工具。WPF / .NET 8 编写，界面参考 Windows 11 设置页。

## 功能

- **去快捷方式小箭头** —— 写入 `Shell Icons\29`，指向 32bpp 全透明图标（1bpp 会被叠加层画成黑块）
- **去桌面图标小盾牌** —— 写入 `Shell Icons\77`，指向系统自带的透明图标 `shell32.dll,50`
- **全局快捷键隐藏 / 显示桌面图标** —— 支持组合键（`Ctrl+Alt+D`）、单个功能键（`F7`）、单个修饰键（右 Alt、左 Ctrl）
- **按键悬浮提示** —— 在鼠标所在屏底部弹出置顶卡片，不抢焦点、不进 Alt+Tab、点击穿透
- **开机自动启动** —— 用计划任务以管理员身份静默启动，不弹 UAC 确认框
- **检查更新** —— 读取 GitHub Releases 的最新版本号，关于页里一键对比
- 深 / 浅色主题，可最小化到托盘常驻
- 退出时自动恢复桌面图标显示

## 下载

到 [Releases](https://github.com/mingyu0401/windesk-mini/releases/latest) 挑一个：

| 文件 | 大小 | 说明 |
|---|---|---|
| `WindeskMini11.exe` | 约 56 MB | 标准版，自带 .NET 运行时，**双击即用** |
| `WindeskMini11-lite.exe` | 约 400 KB | 精简版，需先装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0) |

首次启动会请求管理员权限（要写系统注册表）。

## 使用

1. 「个性化」页打开两个开关，点「立即生效」
2. 「快捷操作」页设置快捷键，默认 `Ctrl+Alt+D`；往下「常规」里可打开开机自启
3. 想还原：「关于 → 恢复默认」，会删掉注册表改动

> 箭头和盾牌都会被 Explorer 缓存，只重启资源管理器经常不生效，所以按钮叫「立即生效」——它会重启 explorer 并清掉 `iconcache*.db`。

## 系统要求

Windows 10 / 11，x64。

## 构建

```bash
# 在项目根目录执行：先发布到临时目录，再覆盖固定文件名
# 标准版：自包含单文件 -> dist/WindeskMini11.exe
dotnet publish src/WindeskMini11/WindeskMini11.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None \
  -p:PublishDir=build/publish-standard/
cp -f build/publish-standard/WindeskMini11.exe dist/WindeskMini11.exe

# 精简版：依赖 .NET 8 桌面运行时 -> dist/WindeskMini11-lite.exe
dotnet publish src/WindeskMini11/WindeskMini11.csproj -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:DebugType=None \
  -p:PublishDir=build/publish-lite/
cp -f build/publish-lite/WindeskMini11.exe dist/WindeskMini11-lite.exe
```

### 关于体积

WPF 自包含发布一般在 60 MB 以上，因为要把 `Microsoft.WindowsDesktop.App` 整个运行时打包进去。
但这个运行时包的 `RuntimeList.xml` 里没有 Profile 标记，所以哪怕项目不用 WinForms，
`System.Windows.Forms` 全家（约 23 MB）也会被一起拷进来。

本项目的托盘用 `Shell_NotifyIcon` 直接实现，不依赖 WinForms，因此在 `WindeskMini11.csproj`
里加了一个 target 把这批用不到的程序集从发布列表剔除，单文件从 63 MB 降到 56 MB。
再小就只能改框架依赖方式（400 KB），代价是要装运行时。
