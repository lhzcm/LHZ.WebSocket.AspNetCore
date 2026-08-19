using System;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Thrown when a WebSocket upgrade request fails the RFC 6455 handshake validation
/// (for example a missing <c>Sec-WebSocket-Key</c> header or an unsupported
/// <c>Sec-WebSocket-Version</c>). The middleware converts this exception into a
/// 400 Bad Request response.
/// </summary>
public class WebSocketHandshakeException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="WebSocketHandshakeException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public WebSocketHandshakeException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebSocketHandshakeException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public WebSocketHandshakeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
