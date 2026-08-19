using System.Threading.Tasks;
using LHZ.WebSocket.Interfaces;

namespace LHZ.WebSocket.AspNetCore;

/// <summary>
/// Extension methods for <see cref="IHttpContext"/>.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Performs the WebSocket handshake and creates a WebSocket client asynchronously.
    /// Delegates to <see cref="Http.HttpContext.HttpUpgradeAsync"/> when the context is
    /// the ASP.NET Core implementation; falls back to the synchronous handshake otherwise.
    /// </summary>
    /// <param name="context">The upgrade context.</param>
    /// <param name="capacity">The receive buffer capacity.</param>
    /// <returns>A task that completes with the created WebSocket client.</returns>
    public static Task<WebSocketClient> HttpUpgradeAsync(this IHttpContext context, int capacity = 1024)
    {
        if (context is Http.HttpContext httpContext)
        {
            return httpContext.HttpUpgradeAsync(capacity);
        }
        return Task.FromResult(context.HttpUpgrade(capacity));
    }
}
