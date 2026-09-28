using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LHZ.WebSocket.Enums;
using LHZ.WebSocket.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;

namespace LHZ.WebSocket.AspNetCore.Http;

/// <summary>
/// Represents a WebSocket upgrade context for ASP.NET Core requests.
/// </summary>
public class HttpContext : IHttpContext
{
    /// <summary>The GUID from RFC 6455 §4.2.2 used to derive <c>Sec-WebSocket-Accept</c>.</summary>
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>RFC 6455 §4.1: <c>Sec-WebSocket-Key</c> is a base64-encoded 16-byte nonce.</summary>
    private const int SecWebSocketKeyLength = 16;

    private readonly Microsoft.AspNetCore.Http.HttpContext _httpContext;
    private readonly TaskCompletionSource _tcs;
    private readonly IApplicationBuilder _app;
    private readonly WebSocket.Http.HttpRequest _request;
    private readonly WebSocket.Http.HttpResponse _response;
    private Stream? _stream;

    /// <summary>
    /// Backing store for <see cref="Status"/>, held as an <see cref="int"/> so transitions can be
    /// performed atomically with <see cref="Interlocked"/> from both the request thread and the
    /// handshake timeout watcher.
    /// </summary>
    private int _status = (int)HttpContextStatus.NotInitialized;

    private WebSocketClient _webSocketClient = null!;
    private CancellationTokenSource? _timeOutCts;
    private int _disposed;

    /// <summary>The parsed HTTP upgrade request.</summary>
    public WebSocket.Http.HttpRequest Request => _request;
    /// <summary>The HTTP response used for the handshake (101 Switching Protocols).</summary>
    public WebSocket.Http.HttpResponse Response => _response;
    /// <summary>The current status of the upgrade context.</summary>
    public HttpContextStatus Status => (HttpContextStatus)Volatile.Read(ref _status);
    /// <summary>The WebSocket client created by <see cref="HttpUpgrade(int)"/> or <see cref="HttpUpgradeAsync(int)"/>.</summary>
    public WebSocketClient WebSocketClient => _webSocketClient;
    /// <summary>Task completed when this context is disposed (rejected) or its client closes.</summary>
    public TaskCompletionSource TaskCompletionSource => _tcs;
    /// <summary>The upgraded duplex stream, or <c>null</c> before the handshake completes.</summary>
    public Stream Stream => _stream!;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContext"/> class.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="httpContext">The ASP.NET Core HttpContext.</param>
    private HttpContext(IApplicationBuilder app, Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        _app = app;
        _httpContext = httpContext;

        var headers = new WebSocket.Http.HttpHeaders();
        foreach (var header in _httpContext.Request.Headers)
        {
            foreach (var value in header.Value)
            {
                headers.Add(header.Key, value);
            }
        }

        _request = new WebSocket.Http.HttpRequest(_httpContext.Request.GetDisplayUrl(), _httpContext.Request.Method, _httpContext.Request.Protocol, headers);
        _response = new WebSocket.Http.HttpResponse(HttpStatusCode.SwitchingProtocols, "HTTP/1.1");
        _tcs = new TaskCompletionSource();
    }

    /// <summary>
    /// Builds and initializes the WebSocket upgrade context for the current request.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="httpContext">The ASP.NET Core HttpContext.</param>
    /// <param name="timeOut">Timeout in seconds for the upgrade request.</param>
    /// <returns>The initialized WebSocket context.</returns>
    public static HttpContext GetHttpContext(IApplicationBuilder app, Microsoft.AspNetCore.Http.HttpContext httpContext, int timeOut)
    {
        var context = new HttpContext(app, httpContext);
        context.Init(timeOut);
        return context;
    }

    /// <summary>
    /// Starts the upgrade request timeout watcher.
    /// </summary>
    /// <param name="timeOut">Timeout in seconds.</param>
    protected void Init(int timeOut)
    {
        Volatile.Write(ref _status, (int)HttpContextStatus.Initialized);
        if (timeOut <= 0)
        {
            return;
        }

        // The watcher is cancelled as soon as the handshake is claimed or the context is
        // disposed, so a completed request does not leave a pending delay behind.
        var cts = new CancellationTokenSource();
        _timeOutCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(timeOut), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Only abort while the handshake has not been claimed yet; once the status moves to
            // Upgrading the stream belongs to the client and must not be torn down here.
            if (Interlocked.CompareExchange(
                    ref _status,
                    (int)HttpContextStatus.TimedOut,
                    (int)HttpContextStatus.Initialized) == (int)HttpContextStatus.Initialized)
            {
                _httpContext.Abort();
                _tcs.TrySetResult();
            }
        });
    }

    /// <summary>
    /// Releases the context: stops the timeout watcher, tears down the upgraded stream and
    /// completes the request. Calling this before the handshake rejects the upgrade.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once, and from several threads at once. A context that already
    /// reached <see cref="HttpContextStatus.Upgraded"/> keeps that status, so a connection that
    /// served traffic is not retroactively reported as rejected.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timeOutCts?.Cancel();
        _timeOutCts?.Dispose();
        _timeOutCts = null;

        // Upgraded and TimedOut are terminal; only a context that never completed its
        // handshake becomes Rejected.
        Interlocked.CompareExchange(
            ref _status,
            (int)HttpContextStatus.Rejected,
            (int)HttpContextStatus.Initialized);
        Interlocked.CompareExchange(
            ref _status,
            (int)HttpContextStatus.Rejected,
            (int)HttpContextStatus.NotInitialized);

        _stream?.Dispose();
        _tcs.TrySetResult();
    }

    /// <summary>
    /// Performs the WebSocket handshake and creates a WebSocket client.
    /// </summary>
    /// <param name="capacity">The receive buffer capacity.</param>
    /// <returns>The created WebSocket client.</returns>
    /// <remarks>
    /// This overload blocks the request thread while the server flushes the handshake response.
    /// Prefer <see cref="HttpUpgradeAsync(int)"/>, which does the same work without occupying a
    /// thread pool thread.
    /// </remarks>
    [Obsolete("Use HttpUpgradeAsync instead; the synchronous handshake blocks a thread pool thread and can starve the pool under load.")]
    public WebSocketClient HttpUpgrade(int capacity = 1024)
    {
        var upgradeFeature = PrepareUpgrade();
        _stream = upgradeFeature.UpgradeAsync().GetAwaiter().GetResult();
        CompleteUpgrade();

        return CreateWebSocketClient(capacity);
    }

    /// <summary>
    /// Performs the WebSocket handshake and creates a WebSocket client.
    /// </summary>
    /// <param name="capacity">The receive buffer capacity.</param>
    /// <returns>The created WebSocket client.</returns>
    public async Task<WebSocketClient> HttpUpgradeAsync(int capacity = 1024)
    {
        var upgradeFeature = PrepareUpgrade();
        _stream = await upgradeFeature.UpgradeAsync();
        CompleteUpgrade();

        return CreateWebSocketClient(capacity);
    }

    /// <summary>
    /// Validates the handshake, claims the context for upgrading, writes the handshake response
    /// headers and returns the feature used to take over the connection.
    /// </summary>
    /// <returns>The HTTP upgrade feature for the current request.</returns>
    /// <exception cref="WebSocketHandshakeException">The request is not a valid RFC 6455 handshake.</exception>
    /// <exception cref="InvalidOperationException">The context cannot be upgraded any more.</exception>
    private IHttpUpgradeFeature PrepareUpgrade()
    {
        // Validate before claiming the context so a rejected handshake stays in Initialized and
        // the middleware can still turn it into a 400 response.
        string secWebSocketKey = ValidateHandshakeRequest();

        var upgradeFeature = _httpContext.Features.Get<IHttpUpgradeFeature>();
        if (upgradeFeature == null)
        {
            throw new InvalidOperationException("HTTP upgrade feature is not available.");
        }

        // Claim the handshake exactly once, which also stops the timeout watcher from aborting
        // a connection that is about to be handed to a client.
        var previous = (HttpContextStatus)Interlocked.CompareExchange(
            ref _status,
            (int)HttpContextStatus.Upgrading,
            (int)HttpContextStatus.Initialized);
        if (previous != HttpContextStatus.Initialized)
        {
            throw new InvalidOperationException($"The WebSocket upgrade cannot be performed because the context is in the '{previous}' state.");
        }

        WriteUpgradeResponse(secWebSocketKey);
        return upgradeFeature;
    }

    /// <summary>
    /// Marks the handshake as finished and stops the timeout watcher.
    /// </summary>
    private void CompleteUpgrade()
    {
        Volatile.Write(ref _status, (int)HttpContextStatus.Upgraded);
        _timeOutCts?.Cancel();
        _timeOutCts?.Dispose();
        _timeOutCts = null;
    }

    /// <summary>
    /// Validates the RFC 6455 handshake headers of the incoming request.
    /// </summary>
    /// <returns>The validated <c>Sec-WebSocket-Key</c> value.</returns>
    /// <exception cref="WebSocketHandshakeException">A required header is missing or malformed.</exception>
    private string ValidateHandshakeRequest()
    {
        // RFC 6455 §4.2.1: the request must carry "Connection: Upgrade". The field is a
        // comma-separated list of case-insensitive tokens.
        if (!Request.Headers.TryGetValues("Connection", out var connectionValues) ||
            !connectionValues.Any(value => value != null && value.Split(',').Any(part => string.Equals(part.Trim(), "Upgrade", StringComparison.OrdinalIgnoreCase))))
        {
            throw new WebSocketHandshakeException("The 'Connection' header must contain the 'Upgrade' token.");
        }

        if (!Request.Headers.TryGetValues("Sec-WebSocket-Key", out var keyValues))
        {
            throw new WebSocketHandshakeException("The 'Sec-WebSocket-Key' header is missing.");
        }
        string secWebSocketKey = keyValues.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrEmpty(secWebSocketKey))
        {
            throw new WebSocketHandshakeException("The 'Sec-WebSocket-Key' header is empty.");
        }

        // RFC 6455 §4.1: the key must be a base64-encoded 16-byte value.
        var decoded = new byte[SecWebSocketKeyLength];
        if (!Convert.TryFromBase64String(secWebSocketKey, decoded, out int decodedLength) ||
            decodedLength != SecWebSocketKeyLength)
        {
            throw new WebSocketHandshakeException($"The 'Sec-WebSocket-Key' header must be a base64-encoded {SecWebSocketKeyLength}-byte value.");
        }

        // RFC 6455 §4.2.1: the server MUST fail the connection if the version is not 13.
        if (!Request.Headers.TryGetValues("Sec-WebSocket-Version", out var versionValues) ||
            !string.Equals(versionValues.FirstOrDefault(), "13", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebSocketHandshakeException("The 'Sec-WebSocket-Version' header must be '13'.");
        }

        return secWebSocketKey;
    }

    /// <summary>
    /// Computes the accept key (RFC 6455 §4.2.2) and writes the handshake response headers so the
    /// server completes the 101 Switching Protocols handshake.
    /// </summary>
    /// <param name="secWebSocketKey">The validated <c>Sec-WebSocket-Key</c> value.</param>
    private void WriteUpgradeResponse(string secWebSocketKey)
    {
        _response.Headers.Add("Upgrade", "websocket");
        _response.Headers.Add("Connection", "Upgrade");

        var acceptKey = Convert.ToBase64String(
            SHA1.HashData(
                Encoding.UTF8.GetBytes(secWebSocketKey + WebSocketGuid)));
        _response.Headers.Add("Sec-WebSocket-Accept", acceptKey);

        foreach (var item in _response.Headers)
        {
            foreach (var value in item.Value)
            {
                _httpContext.Response.Headers.Append(item.Key, value);
            }
        }
    }

    /// <summary>
    /// Creates the WebSocket client, registers it with the application and wires up
    /// the close handler that unregisters the client and completes the request.
    /// </summary>
    /// <param name="capacity">The receive buffer capacity.</param>
    /// <returns>The created WebSocket client.</returns>
    private WebSocketClient CreateWebSocketClient(int capacity)
    {
        _webSocketClient = new WebSocketClient(this, capacity);
        _webSocketClient.OnClientClose += (c) =>
        {
            WebSocketBuilderExtensions.RemoveWebSocketClient(_app, _webSocketClient);
            this.Dispose();
        };

        WebSocketBuilderExtensions.AddWebSocketClient(_app, _webSocketClient);
        return _webSocketClient;
    }
}
