# LHZ.WebSocket.AspNetCore

English | [中文](README.zh-CN.md)

A lightweight ASP.NET Core middleware that integrates the `LHZ.WebSocket` library to handle HTTP-to-WebSocket upgrades and manage active WebSocket clients.

## Overview

This library exposes a minimal middleware extension `UseWebSocket` for ASP.NET Core applications. It wraps `LHZ.WebSocket` primitives and provides an `IHttpContext` abstraction so applications can accept WebSocket upgrades, create `WebSocketClient` instances, and manage client lifecycles.

## Features

- `UseWebSocket` middleware for handling WebSocket upgrade requests
- `IHttpContext` wrapper that performs the RFC6455 handshake (with `Sec-WebSocket-Key` / `Sec-WebSocket-Version` validation) and returns a `WebSocketClient`
- Synchronous and asynchronous upgrade APIs: `HttpUpgrade` / `HttpUpgradeAsync`
- Thread-safe registration and removal of connected clients
- Optional upgrade timeout control

## Requirements

- .NET 5 / 6 / 8 / 9 / 10
- `LHZ.WebSocket` package (version `1.1.1`)

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
    client.OnCloseRecived += (c, reason) => c.Close();
});

app.Run();
```

Notes:

- The delegate is invoked only when the request carries a WebSocket `Upgrade` header (matched case-insensitively per RFC 7230).
- Call `context.HttpUpgrade()` or `await context.HttpUpgradeAsync()` to perform the handshake and create a `WebSocketClient`. Invalid handshake requests (missing `Sec-WebSocket-Key` or a version other than 13) are rejected with a `400 Bad Request` response.
- Use `app.GetWebSocketClientCount()` and `app.GetWebSocketClients()` to inspect active clients.

## API Summary

- `UseWebSocket(WebSocketUpgradeDelegate webSocketUpgradeDelegate, int timeOut = 10)` — Adds middleware to handle upgrades; `timeOut` limits seconds to wait for an upgrade.
- `UseWebSocket(Func<IHttpContext, Task> webSocketUpgradeDelegate, int timeOut = 10)` — Async overload of the same middleware.
- `GetWebSocketClients()` — Returns active `WebSocketClient` instances for the `IApplicationBuilder`.
- `GetWebSocketClientCount()` — Returns the number of active clients for the `IApplicationBuilder`.

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
