# LHZ.WebSocket.AspNetCore

English | [中文](README.zh-CN.md)

A lightweight ASP.NET Core middleware that integrates the `LHZ.WebSocket` library to handle HTTP-to-WebSocket upgrades and manage active WebSocket clients.

## Overview

This library exposes a minimal middleware extension `UseWebSocket` for ASP.NET Core applications. It wraps `LHZ.WebSocket` primitives and provides an `IHttpContext` abstraction so applications can accept WebSocket upgrades, create `WebSocketClient` instances, and manage client lifecycles.

## Features

- `UseWebSocket` middleware for handling WebSocket upgrade requests
- `IHttpContext` wrapper that performs the RFC6455 handshake (validating `Connection`, `Sec-WebSocket-Key` and `Sec-WebSocket-Version`) and returns a `WebSocketClient`
- Asynchronous upgrade API `HttpUpgradeAsync` (the synchronous `HttpUpgrade` is obsolete)
- Thread-safe registration and removal of connected clients, shared across branched pipelines
- Optional upgrade timeout control

## Requirements

- .NET 5 / 6 / 8 / 9 / 10
- `LHZ.WebSocket` package (version `1.2.0`)

## Installation

Install from NuGet:

```bash
dotnet add package LHZ.WebSocket.AspNetCore
```

## Usage

Register the middleware in the ASP.NET Core pipeline. The delegate receives an `IHttpContext` for the upgrade request.

```csharp
using System;
using LHZ.WebSocket.AspNetCore;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSocket(async context =>
{
    // Perform the handshake and obtain a WebSocket client
    var client = await context.HttpUpgradeAsync();

    client.OnMessageReceived += (c, message) => c.SendMessage($"Echo: {message}");
    client.OnCloseReceived += (c, reason) => c.Close();
});

app.Run();
```

Notes:

- The delegate is invoked only when the request carries a WebSocket `Upgrade` header. The token is matched case-insensitively and is also recognised inside a comma-separated field such as `Upgrade: h2c, websocket` (RFC 7230).
- Call `await context.HttpUpgradeAsync()` to perform the handshake and create a `WebSocketClient`. Invalid handshake requests are rejected with a `400 Bad Request` response: a `Connection` header without the `Upgrade` token, a missing `Sec-WebSocket-Key` or one that is not a base64-encoded 16-byte value, or a `Sec-WebSocket-Version` other than 13.
- A context can only be upgraded once; a second attempt throws `InvalidOperationException`.
- Use `app.GetWebSocketClientCount()` and `app.GetWebSocketClients()` to inspect active clients.

## API Summary

- `UseWebSocket(WebSocketUpgradeDelegate webSocketUpgradeDelegate, int timeOut = 10)` — Adds middleware to handle upgrades; `timeOut` limits seconds to wait for an upgrade.
- `UseWebSocket(Func<IHttpContext, Task> webSocketUpgradeDelegate, int timeOut = 10)` — Async overload of the same middleware.
- `GetWebSocketClients()` — Returns the active `WebSocketClient` instances for the application. Clients are tracked per application, so those registered from a pipeline branch built by `Map` or `UseWhen` are also returned here.
- `GetWebSocketClientCount()` — Returns the number of active clients for the application.

## Example

See `src/LHZ.WebSocket.AspNetCore.Console/Program.cs` for a runnable example that logs messages and client counts.

## Development

Build the solution:

```bash
dotnet build src/LHZ.WebSocket.AspNetCore.slnx
```

Run tests:

```bash
dotnet test src/LHZ.WebSocket.AspNetCore.Test/LHZ.WebSocket.AspNetCore.Test.csproj
```

## Contributing

Contributions and issues are welcome. Please open pull requests or issues at the repository URL.

## License

MIT
