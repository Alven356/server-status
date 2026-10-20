# server-status

**把 Minecraft 服务器的在线变化，安静地放进菜单栏。**

server-status 是一款面向 macOS 与 Windows 的轻量级 Minecraft Java 服务器守望工具。它常驻菜单栏或系统托盘，按设定间隔检查服务器状态，在服务器从离线恢复为在线时提醒你。无需一直打开网页，也不会用重复通知打扰你。

## 下载

前往 [Releases](https://github.com/Alven356/server-status/releases/latest) 获取最新正式版：

- **Apple Silicon Mac**：选择 `server-status-2.1.0-mac-arm64.dmg`
- **Intel Mac**：选择 `server-status-2.1.0-mac-x64.dmg`
- **Windows 11 x64**：选择 `server-status-2.1.0-win-x64.exe`

每个正式版本都附带 SHA-256 校验清单。macOS 构建目前使用本地临时签名，尚未经过 Apple 公证；Windows 安装包目前未使用商业代码签名。

## 核心功能

- 菜单栏或系统托盘常驻，不占用任务栏主窗口。
- 清晰区分在线、离线与“暂时无法确认”，避免把网络异常误报为离线。
- 显示在线玩家数、响应延迟与服务器 MOTD。
- 仅在服务器从离线变为在线时发送系统通知与提示音。
- 支持 15 秒至 5 分钟的检查间隔以及登录时自动启动。
- 识别 Aternos 常见离线响应，包括 `This server is offline` 与 `craft.link/ram` 提示。
- 支持 macOS Intel、macOS Apple Silicon 与 Windows 11 x64。

## 使用方法

1. 安装并启动 server-status。
2. 单击菜单栏或系统托盘中的状态图标。
3. 打开“设置”，填写 Minecraft Java 服务器地址并选择检查频率。
4. 根据需要启用系统通知、提示音与登录时启动。
5. 保存后关闭设置窗口；守望会继续在后台运行。

状态图标会随服务器状态变化。单击图标可随时查看当前状态，再次单击或点到面板外即可收起。

## 隐私与网络

设置只保存在本机。应用不会读取或上传 Minecraft 账号资料。状态检查优先使用 Minecraft Java 状态协议直连服务器；为降低本地网络限制造成误判的概率，还会使用 `ping.cornbread2100.com` 与 `mcstatus.io` 对服务器地址进行复核。

## 开发与测试

项目使用 C#、.NET 8 与 Avalonia 构建，不依赖 Electron 或网页运行时。

```sh
dotnet test native/ServerStatus.Tests/ServerStatus.Tests.csproj -c Release
dotnet run --project native/ServerStatus/ServerStatus.csproj
```

在 macOS 上运行 `packaging/build-release.sh` 可生成三个平台安装包及 SHA-256 清单。

## 版本

当前开发版本：**2.1.0** — 正式产品界面、清晰的设置分区与发布说明。
