using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Provides extension methods for WebSocket middleware and client tracking.
/// </summary>
public static class WebSocketBuilderExtensions
{
    private static ConcurrentDictionary<IApplicationBuilder, ConcurrentDictionary<Guid, WebSocketClient>> _webSocketClients = new ConcurrentDictionary<IApplicationBuilder, ConcurrentDictionary<Guid, WebSocketClient>>();

    /// <summary>
    /// Registers a new WebSocket client for the current application builder.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="client">The WebSocket client to register.</param>
    internal static void AddWebSocketClient(IApplicationBuilder app, WebSocketClient client)
    {
        if (_webSocketClients.TryGetValue(app, out var clients))
        {
            client.OnClientClose += (c) =>
            {
                clients.TryRemove(c.ID, out _);
            };
            clients[client.ID] = client;
        }
    }

    /// <summary>
    /// Removes a WebSocket client when it is closed.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="client">The WebSocket client to remove.</param>
    internal static void RemoveWebSocketClient(IApplicationBuilder app, WebSocketClient client)
    {
        if (_webSocketClients.TryGetValue(app, out var clients))
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
        if (_webSocketClients.TryGetValue(app, out var clients))
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
        if (_webSocketClients.TryGetValue(app, out var clients))
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
        if(!_webSocketClients.TryGetValue(app, out var clients))
        {
            _webSocketClients[app] = new ConcurrentDictionary<Guid, WebSocketClient>();
        }

        app.Use(async (context, next) =>
        {
            var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();
            if (upgradeFeature != null)
            {
                var headers = new LHZ.WebSocket.Http.HttpHeaders();
                foreach (var header in context.Request.Headers)
                {
                    foreach(var value in header.Value)
                    {
                        headers.Add(header.Key, value);
                    }
                }

                var httpRequest = new LHZ.WebSocket.Http.HttpRequest(context.Request.GetDisplayUrl(), context.Request.Method, context.Request.Protocol, headers);
                var httpContext =  LHZ.WebSocket.AspNetCore.Http.HttpContext.GetHttpContext(app, context, timeOut);

                webSocketUpgradeDelegate(httpContext);

                if(httpContext.Status == HttpContextStatus.Upgraded)
                {
                    httpContext.WebSocketClient.Open();
                }
                else
                {
                    httpContext.Dispose();
                }

                await httpContext.TaskCompletionSource.Task;
            }
            else
            {
                await next();
            }
        });

        return app;
    }
}
