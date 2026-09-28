using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Thread-safe store of the WebSocket clients currently connected to one application.
/// </summary>
/// <remarks>
/// One registry exists per application (keyed by the root <see cref="IServiceProvider"/>), so
/// clients registered from a branched pipeline built by <c>Map</c>/<c>UseWhen</c> are still
/// visible through the outer application builder.
/// </remarks>
internal sealed class WebSocketClientRegistry
{
    private readonly ConcurrentDictionary<Guid, WebSocketClient> _clients = new ConcurrentDictionary<Guid, WebSocketClient>();

    /// <summary>The number of currently registered clients.</summary>
    public int Count => _clients.Count;

    /// <summary>Registers a client, replacing any previous entry with the same ID.</summary>
    /// <param name="client">The client to register.</param>
    public void Add(WebSocketClient client) => _clients[client.ID] = client;

    /// <summary>Unregisters a client. Does nothing when the client is not registered.</summary>
    /// <param name="client">The client to unregister.</param>
    public void Remove(WebSocketClient client) => _clients.TryRemove(client.ID, out _);

    /// <summary>Takes a snapshot of the registered clients.</summary>
    /// <returns>The currently registered clients.</returns>
    public IEnumerable<WebSocketClient> Snapshot() => _clients.Values.ToArray();
}
