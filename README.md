# server-status

使用 C#、.NET 8 与 Avalonia 编写的原生桌面 Minecraft Java 服务器状态提醒器。发布目标为 macOS Ventura 13.4（Intel 与 Apple 芯片）和 Windows 11 x64；正式程序不使用 Electron、HTML、CSS 或网页布局。

## 行为

- 打开后直接驻留菜单栏/系统托盘，不主动显示主窗口。
- 单击红、绿或灰色圆环图标即可打开状态面板；再次单击或点到外部会收起。
- 状态面板不带关闭按钮，在全屏空间中也会显示在当前窗口上方。
- 在线显示玩家人数、实时延迟和 MOTD；网络无法确认时不会伪装成离线。
- Aternos 返回 `This server is offline` 或带 `craft.link/ram` 的离线 MOTD 时，明确显示离线。
- 仅在离线变为在线时发送系统通知与提示音，不会在每次启动时重复提醒。
- 设置窗口可调整地址、15 秒至 5 分钟的检查频率、通知、声音与登录启动。

## 本地构建与测试

需要 .NET 8 SDK。

```sh
dotnet test native/ServerStatus.Tests/ServerStatus.Tests.csproj -c Release
dotnet run --project native/ServerStatus/ServerStatus.csproj
```

macOS 上运行 `packaging/build-release.sh` 会生成：

- `server-status-2.0.1-mac-x64.dmg`
- `server-status-2.0.1-mac-arm64.dmg`
- `server-status-2.0.1-win-x64.exe`

## 隐私

设置只保存在本机。应用优先使用 Minecraft Java 状态协议直连服务器；为避免本地网络屏蔽导致误判，会并行使用 `ping.cornbread2100.com` 与 `mcstatus.io` 复核服务器地址，不会上传 Minecraft 账号或其他个人资料。
