# Windesk Mini

Windows 11 桌面图标美化小工具。WPF / .NET 8 编写，界面参考 Windows 11 设置页。

## 功能

- **去快捷方式小箭头** —— 写入 `Shell Icons\29`，指向 1bpp 全透明图标
- **去桌面图标小盾牌** —— 写入 `Shell Icons\77`，指向系统自带的透明图标 `shell32.dll,50`
- **全局快捷键隐藏 / 显示桌面图标** —— 支持组合键（`Ctrl+Alt+D`）、单个功能键（`F7`）、单个修饰键（右 Alt、左 Ctrl）
- **按键悬浮提示** —— 在鼠标所在屏底部弹出置顶卡片，不抢焦点、不进 Alt+Tab、点击穿透
- **开机自动启动** —— 用计划任务以管理员身份静默启动，不弹 UAC 确认框
- 深 / 浅色主题，可最小化到托盘常驻
- 退出时自动恢复桌面图标显示

## 下载

到 [Releases](https://github.com/mingyu0401/windesk-mini/releases/latest) 下载 `WindeskMini11-v1.3.0.exe`，双击运行。

单文件自包含，不需要预装 .NET。首次启动会请求管理员权限（要写系统注册表）。

## 使用

1. 「个性化」页打开两个开关，点「立即生效」
2. 「快捷操作」页设置快捷键，默认 `Ctrl+Alt+D`；往下「常规」里可打开开机自启
3. 想还原：「关于 → 恢复默认」，会删掉注册表改动

> 箭头和盾牌都会被 Explorer 缓存，只重启资源管理器经常不生效，所以按钮叫「立即生效」——它会重启 explorer 并清掉 `iconcache*.db`。

## 系统要求

Windows 10 / 11，x64。

## 构建

```bash
cd src/WindeskMini11
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o ../../dist
```
