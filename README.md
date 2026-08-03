# LHZ.WebSocket.AspNetCore

English | [中文](README.zh-CN.md)

A lightweight ASP.NET Core middleware that integrates the `LHZ.WebSocket` library to handle HTTP-to-WebSocket upgrades and manage active WebSocket clients.

## Overview

This library exposes a minimal middleware extension `UseWebSocket` for ASP.NET Core applications. It wraps `LHZ.WebSocket` primitives and provides an `IHttpContext` abstraction so applications can accept WebSocket upgrades, create `WebSocketClient` instances, and manage client lifecycles.

## Features

- `UseWebSocket` middleware for handling WebSocket upgrade requests
- `IHttpContext` wrapper that performs the RFC6455 handshake and returns a `WebSocketClient`
- Thread-safe registration and removal of connected clients
- Optional upgrade timeout control

## Requirements

- .NET 5 / 6 / 8 / 9 / 10
- `LHZ.WebSocket` package (example: `1.0.2`)

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

app.UseWebSocket(context =>
{
    // Perform the handshake and obtain a WebSocket client
    var client = context.HttpUpgrade();

    client.OnMessageReceived += (c, message) => c.SendMessage($"Echo: {message}");
    client.OnCloseRecived += (c, reason) => c.Close();
});

app.Run();
```

Notes:

- The delegate is invoked only when the request contains an HTTP upgrade for WebSocket.
- Call `context.HttpUpgrade()` to perform the handshake and create a `WebSocketClient`.
- Use `app.GetWebSocketClientCount()` and `app.GetWebSocketClients()` to inspect active clients.

## API Summary

- `UseWebSocket(WebSocketUpgradeDelegate webSocketUpgradeDelegate, int timeOut = 10)` — Adds middleware to handle upgrades; `timeOut` limits seconds to wait for an upgrade.
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
