using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Provides extension methods for WebSocket middleware and client tracking.
/// </summary>
public static class WebSocketBuilderExtensions
{
    /// <summary>
    /// Key under which the per-application client store is kept in <see cref="IApplicationBuilder.Properties"/>.
    /// Storing the store on the builder itself (instead of a static field) keeps client
    /// registrations isolated per application and avoids leaking application instances.
    /// </summary>
    private const string WebSocketClientsKey = "LHZ.WebSocket.AspNetCore.Clients";

    private static readonly object _storeLock = new object();

    /// <summary>
    /// Registers a new WebSocket client for the current application builder.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="client">The WebSocket client to register.</param>
    internal static void AddWebSocketClient(IApplicationBuilder app, WebSocketClient client)
    {
        GetOrCreateWebSocketClients(app)[client.ID] = client;
    }

    /// <summary>
    /// Removes a WebSocket client when it is closed.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="client">The WebSocket client to remove.</param>
    internal static void RemoveWebSocketClient(IApplicationBuilder app, WebSocketClient client)
    {
        if (app.Properties.TryGetValue(WebSocketClientsKey, out var value) && value is ConcurrentDictionary<Guid, WebSocketClient> clients)
        {
            clients.TryRemove(client.ID, out _);
        }
    }

    /// <summary>
    /// Returns all active WebSocket clients for this application builder.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>Active WebSocket clients.</returns>
    public static IEnumerable<WebSocketClient> GetWebSocketClients(this IApplicationBuilder app)
    {
        if (app.Properties.TryGetValue(WebSocketClientsKey, out var value) && value is ConcurrentDictionary<Guid, WebSocketClient> clients)
        {
            return clients.Values.ToArray();
        }
        return Array.Empty<WebSocketClient>();
    }

    /// <summary>
    /// Returns the current count of active WebSocket clients.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>The number of active WebSocket clients.</returns>
    public static int GetWebSocketClientCount(this IApplicationBuilder app)
    {
        if (app.Properties.TryGetValue(WebSocketClientsKey, out var value) && value is ConcurrentDictionary<Guid, WebSocketClient> clients)
        {
            return clients.Count;
        }
        return 0;
    }

    /// <summary>
    /// Adds middleware to handle HTTP upgrade requests for WebSocket.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="webSocketUpgradeDelegate">Delegate called for each WebSocket upgrade request.</param>
    /// <param name="timeOut">Timeout in seconds for WebSocket upgrade handling.</param>
    /// <returns>The application builder instance.</returns>
    public static IApplicationBuilder UseWebSocket(this IApplicationBuilder app, WebSocketUpgradeDelegate webSocketUpgradeDelegate, int timeOut = 10)
    {
        if (webSocketUpgradeDelegate == null)
        {
            throw new ArgumentNullException(nameof(webSocketUpgradeDelegate));
        }
        return UseWebSocket(app, context =>
        {
            webSocketUpgradeDelegate(context);
            return Task.CompletedTask;
        }, timeOut);
    }

    /// <summary>
    /// Adds middleware to handle HTTP upgrade requests for WebSocket with an asynchronous delegate.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="webSocketUpgradeDelegate">Async delegate called for each WebSocket upgrade request.</param>
    /// <param name="timeOut">Timeout in seconds for WebSocket upgrade handling.</param>
    /// <returns>The application builder instance.</returns>
    public static IApplicationBuilder UseWebSocket(this IApplicationBuilder app, Func<IHttpContext, Task> webSocketUpgradeDelegate, int timeOut = 10)
    {
        if (webSocketUpgradeDelegate == null)
        {
            throw new ArgumentNullException(nameof(webSocketUpgradeDelegate));
        }
        GetOrCreateWebSocketClients(app);
        app.Use(async (context, next) =>
        {
            // Check if the request is a WebSocket upgrade request.
            // RFC 7230 §3.2.6: field values are case-insensitive tokens, so compare ignoring case.
            if (!context.Request.Headers.TryGetValue("Upgrade", out var upgradeValue) ||
                !string.Equals(upgradeValue, "websocket", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var httpContext = Http.HttpContext.GetHttpContext(app, context, timeOut);
            try
            {
                // Call the provided delegate to handle the WebSocket upgrade request.
                await webSocketUpgradeDelegate(httpContext);
            }
            catch (WebSocketHandshakeException)
            {
                // Invalid handshake request (missing/invalid Sec-WebSocket-* headers):
                // reject with 400 Bad Request instead of failing with a 500.
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }

            if (httpContext.Status == HttpContextStatus.Upgraded)
            {
                httpContext.WebSocketClient.Open();
            }
            else
            {
                httpContext.Dispose();
            }

            // Keep the request alive until the WebSocket connection is closed.
            await httpContext.TaskCompletionSource.Task;
        });
        return app;
    }

    /// <summary>
    /// Gets the thread-safe client store for the given application builder, creating it on first use.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>The client store.</returns>
    private static ConcurrentDictionary<Guid, WebSocketClient> GetOrCreateWebSocketClients(IApplicationBuilder app)
    {
        if (app.Properties.TryGetValue(WebSocketClientsKey, out var value) && value is ConcurrentDictionary<Guid, WebSocketClient> clients)
        {
            return clients;
        }
        lock (_storeLock)
        {
            if (app.Properties.TryGetValue(WebSocketClientsKey, out value) && value is ConcurrentDictionary<Guid, WebSocketClient> existing)
            {
                return existing;
            }
            var created = new ConcurrentDictionary<Guid, WebSocketClient>();
            app.Properties[WebSocketClientsKey] = created;
            return created;
        }
    }
}
