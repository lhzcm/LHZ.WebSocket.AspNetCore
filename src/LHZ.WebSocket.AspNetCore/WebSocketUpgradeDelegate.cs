using LHZ.WebSocket.Interfaces;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Handles a WebSocket upgrade request. The delegate receives an <see cref="IHttpContext"/>
/// and typically calls <see cref="IHttpContext.HttpUpgrade(int)"/> to complete the handshake.
/// </summary>
/// <param name="context">The upgrade context for the current request.</param>
public delegate void WebSocketUpgradeDelegate(IHttpContext context);