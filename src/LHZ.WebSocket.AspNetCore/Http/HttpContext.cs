using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
    private Microsoft.AspNetCore.Http.HttpContext _httpContext;
    private readonly TaskCompletionSource _tcs;
    private readonly IApplicationBuilder _app;
    private WebSocket.Http.HttpRequest _request;
    private WebSocket.Http.HttpResponse _response;
    private Stream? _stream;
    private HttpContextStatus _status = HttpContextStatus.NotInitialized;
    private WebSocketClient _webSocketClient = null!;
    private Task? _timeOutExecuter = null;

    public WebSocket.Http.HttpRequest Request => _request;
    public WebSocket.Http.HttpResponse Response => _response;
    public HttpContextStatus Status => _status;
    public WebSocketClient WebSocketClient => _webSocketClient;
    public TaskCompletionSource TaskCompletionSource => _tcs;
    public Stream? Stream => _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContext"/> class.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="httpContext">The ASP.NET Core HttpContext.</param>
    private HttpContext(IApplicationBuilder app, Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        _app = app;
        _httpContext = httpContext;

        var headers = new LHZ.WebSocket.Http.HttpHeaders();
        foreach (var header in _httpContext.Request.Headers)
        {
            foreach (var value in header.Value)
            {
                headers.Add(header.Key, value);
            }
        }

        _request = new LHZ.WebSocket.Http.HttpRequest(_httpContext.Request.GetDisplayUrl(), _httpContext.Request.Method, _httpContext.Request.Protocol, headers);
        _response = new LHZ.WebSocket.Http.HttpResponse(HttpStatusCode.SwitchingProtocols, "HTTP/1.1");
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
        _status = HttpContextStatus.Initialized;
        if (timeOut > 0)
        {
            _timeOutExecuter = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(timeOut));
                if (_status == HttpContextStatus.Initialized)
                {
                    _status = HttpContextStatus.TimedOut;
                    _httpContext.Abort();
                }
            });
        }
    }

    /// <summary>
    /// Rejects the WebSocket upgrade and completes the request.
    /// </summary>
    public void Dispose()
    {
        _status = HttpContextStatus.Rejected;
        if(_tcs.Task.IsCompleted == false)
            _tcs.SetResult();
    }

    /// <summary>
    /// Performs th e WebSocket handshake and creates a WebSocket client.
    /// </summary>
    /// <param name="capacity">The receive buffer capacity.</param>
    /// <returns>The created WebSocket client.</returns>
    public WebSocketClient HttpUpgrade(int capacity = 1024)
    {
        _response.Headers.Add("Upgrade", "websocket");
        _response.Headers.Add("Connection", "Upgrade");

        string secWebSocketKey = Request.Headers.GetValues("Sec-WebSocket-Key").First();
        var sha1 = Convert.ToBase64String(
            SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        _response.Headers.Add("Sec-WebSocket-Accept", sha1);

        foreach(var item in _response.Headers)
        {
            foreach(var value in item.Value)
            {
                _httpContext.Response.Headers.Append(item.Key, value);
            }
        }
        var upgradeFeature = _httpContext.Features.Get<IHttpUpgradeFeature>();
        if(upgradeFeature == null)
        {
            throw new InvalidOperationException("HTTP upgrade feature is not available.");
        }
        _stream = upgradeFeature.UpgradeAsync().Result;
        _status = HttpContextStatus.Upgraded;

        _webSocketClient = new WebSocketClient(this, capacity);
        _webSocketClient.OnClientClose += (c) =>
        {
            WebSocketBuilderExtensions.RemoveWebSocketClient(_app, _webSocketClient);
            this.Dispose();
        };

        WebSocketBuilderExtensions.AddWebSocketClient(_app, _webSocketClient);
        return _webSocketClient;
    }
    /// <summary>
    /// Performs the WebSocket handshake and creates a WebSocket client.
    /// </summary>
    /// <param name="capacity">The receive buffer capacity.</param>
    /// <returns>The created WebSocket client.</returns>
    public async Task<WebSocketClient> HttpUpgradeAsync(int capacity = 1024)
    {
        _response.Headers.Add("Upgrade", "websocket");
        _response.Headers.Add("Connection", "Upgrade");

        string secWebSocketKey = Request.Headers.GetValues("Sec-WebSocket-Key").First();
        var sha1 = Convert.ToBase64String(
            SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        _response.Headers.Add("Sec-WebSocket-Accept", sha1);

        foreach(var item in _response.Headers)
        {
            foreach(var value in item.Value)
            {
                _httpContext.Response.Headers.Append(item.Key, value);
            }
        }
        var upgradeFeature = _httpContext.Features.Get<IHttpUpgradeFeature>();
        if(upgradeFeature == null)
        {
            throw new InvalidOperationException("HTTP upgrade feature is not available.");
        }
        _stream =  await upgradeFeature.UpgradeAsync();
        _status = HttpContextStatus.Upgraded;

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
