# LHZ.WebSocket.AspNetCore

[English](README.md) | 中文

轻量级的 ASP.NET Core 中间件，集成 `LHZ.WebSocket` 库以处理 HTTP 到 WebSocket 的升级并管理活动的 WebSocket 客户端。

## 概述

本库提供一个简洁的中间件扩展 `UseWebSocket`，封装 `LHZ.WebSocket` 的基础类型并暴露 `IHttpContext` 抽象，方便应用接受 WebSocket 升级、创建 `WebSocketClient` 并管理客户端生命周期。

## 功能

- `UseWebSocket` 中间件：处理 WebSocket 升级请求
- `IHttpContext`：执行 RFC6455 握手并返回 `WebSocketClient`
- 线程安全的客户端注册与移除
- 可选的升级超时控制

## 要求

- .NET 5 / 6 / 8 / 9 / 10
- 依赖 `LHZ.WebSocket` 包（示例版本：`1.0.2`）

## 安装

如果已发布到 NuGet：

```bash
dotnet add package LHZ.WebSocket.AspNetCore
```

## 用法

在 ASP.NET Core 管道中注册中间件。委托会在检测到升级请求时接收 `IHttpContext`。

```csharp
using System;
using LHZ.WebSocket.AspNetCore;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSocket(context =>
{
    var client = context.HttpUpgrade();
    client.OnMessageReceived += (c, message) => c.SendMessage($"Echo: {message}");
    client.OnCloseRecived += (c, reason) => c.Close();
});

app.Run();
```

注意：

- 仅在请求包含 WebSocket 升级时调用委托（通过 `IHttpUpgradeFeature`）。
- 调用 `context.HttpUpgrade()` 执行握手并创建 `WebSocketClient`。
- 使用 `app.GetWebSocketClientCount()` 和 `app.GetWebSocketClients()` 检查活动客户端。

## API 摘要

- `UseWebSocket(WebSocketUpgradeDelegate webSocketUpgradeDelegate, int timeOut = 10)` — 添加升级中间件；`timeOut` 为等待升级的秒数上限。
- `GetWebSocketClients()` — 返回当前 `IApplicationBuilder` 下的活动 `WebSocketClient` 列表。
- `GetWebSocketClientCount()` — 返回当前 `IApplicationBuilder` 下的活动客户端数量。

## 示例

参见 `src/LHZ.WebSocket.AspNetCore.Console/Program.cs`，演示了日志记录消息和客户数量的可运行示例。

## 开发

构建解决方案：

```bash
dotnet build src/LHZ.WebSocket.AspNetCore.slnx
```

运行测试：

```bash
dotnet test src/LHZ.WebSocket.AspNetCore.Test/LHZ.WebSocket.AspNetCore.Test.csproj
```

## 贡献

欢迎提交 PR 或 issue，详情请见仓库地址。

## 许可证

MIT
