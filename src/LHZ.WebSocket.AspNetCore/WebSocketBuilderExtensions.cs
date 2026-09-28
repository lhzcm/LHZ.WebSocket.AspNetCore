using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Provides extension methods for WebSocket middleware and client tracking.
/// </summary>
public static class WebSocketBuilderExtensions
{
    /// <summary>
    /// Per-application client registries, keyed by the application's root service provider.
    /// Keying on the service provider (rather than on the <see cref="IApplicationBuilder"/> instance
    /// or a static field) keeps registrations isolated per application while still sharing one
    /// registry between the outer builder and the branched builders created by <c>Map</c>/<c>UseWhen</c>.
    /// The table holds weak references to the providers, so applications are not leaked.
    /// </summary>
    private static readonly ConditionalWeakTable<IServiceProvider, WebSocketClientRegistry> _registries =
        new ConditionalWeakTable<IServiceProvider, WebSocketClientRegistry>();

    /// <summary>
    /// Registers a new WebSocket client for the current application builder.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="client">The WebSocket client to register.</param>
    internal static void AddWebSocketClient(IApplicationBuilder app, WebSocketClient client)
    {
        GetOrCreateRegistry(app).Add(client);
    }

    /// <summary>
    /// Removes a WebSocket client when it is closed.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="client">The WebSocket client to remove.</param>
    internal static void RemoveWebSocketClient(IApplicationBuilder app, WebSocketClient client)
    {
        GetOrCreateRegistry(app).Remove(client);
    }

    /// <summary>
    /// Returns all active WebSocket clients for this application.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>Active WebSocket clients.</returns>
    public static IEnumerable<WebSocketClient> GetWebSocketClients(this IApplicationBuilder app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }
        return GetOrCreateRegistry(app).Snapshot();
    }

    /// <summary>
    /// Returns the current count of active WebSocket clients.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>The number of active WebSocket clients.</returns>
    public static int GetWebSocketClientCount(this IApplicationBuilder app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }
        return GetOrCreateRegistry(app).Count;
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
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }
        if (webSocketUpgradeDelegate == null)
        {
            throw new ArgumentNullException(nameof(webSocketUpgradeDelegate));
        }
        GetOrCreateRegistry(app);
        app.Use(async (context, next) =>
        {
            // Check if the request is a WebSocket upgrade request.
            // RFC 7230 §3.2.6: field values are case-insensitive tokens, so compare ignoring case.
            if (!context.Request.Headers.TryGetValue("Upgrade", out var upgradeValue) ||
                !HeaderContainsToken(upgradeValue, "websocket"))
            {
                await next();
                return;
            }

            var httpContext = Http.HttpContext.GetHttpContext(app, context, timeOut);
            try
            {
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
            }
            catch
            {
                // An unexpected failure must not leave the timeout watcher and the upgraded
                // stream behind; Dispose is idempotent so the normal paths above stay correct.
                httpContext.Dispose();
                throw;
            }
        });
        return app;
    }

    /// <summary>
    /// Determines whether a (possibly multi-valued, comma-separated) header contains the given token,
    /// comparing case-insensitively as required by RFC 7230 §3.2.6.
    /// </summary>
    /// <param name="headerValue">The raw header value.</param>
    /// <param name="token">The token to look for.</param>
    /// <returns><c>true</c> when the token is present.</returns>
    internal static bool HeaderContainsToken(StringValues headerValue, string token)
    {
        foreach (var value in headerValue)
        {
            if (value == null)
            {
                continue;
            }
            foreach (var part in value.Split(','))
            {
                if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the client registry for the given application builder, creating it on first use.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>The client registry.</returns>
    private static WebSocketClientRegistry GetOrCreateRegistry(IApplicationBuilder app)
    {
        return _registries.GetValue(app.ApplicationServices, _ => new WebSocketClientRegistry());
    }
}
