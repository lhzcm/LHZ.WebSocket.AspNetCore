using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LHZ.WebSocket.AspNetCore;
using AspNetCoreHttpContext = LHZ.WebSocket.AspNetCore.Http.HttpContext;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LHZ.WebSocket.AspNetCore.Test;

public class WebSocketMiddlewareTests
{
    private static void ConfigureEchoApp(IApplicationBuilder app)
    {
        app.UseWebSocket(async context =>
        {
            var client = await context.HttpUpgradeAsync();
            client.OnMessageReceived += (c, message) => c.SendMessage($"Echo: {message}");
            client.OnCloseRecived += (c, msg) => c.Close();
        });
    }

    private static async Task<(WebApplication App, string BaseAddress)> StartEchoServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        ConfigureEchoApp(app);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        return (app, address);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }
            await Task.Delay(50);
        }
    }

    private static Uri ToWebSocketUri(string baseAddress) =>
        new Uri(baseAddress.Replace("http://", "ws://"));

    [Fact]
    public async Task UseWebSocket_EchoRoundTrip_TracksClientLifecycle()
    {
        var (app, baseAddress) = await StartEchoServerAsync();
        try
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(ToWebSocketUri(baseAddress), CancellationToken.None);

            // The client sees the 101 response before the server finishes registering
            // the client, so wait for the registration instead of asserting immediately.
            await WaitUntilAsync(() => app.GetWebSocketClientCount() == 1, TimeSpan.FromSeconds(5));
            Assert.Equal(1, app.GetWebSocketClientCount());

            // Send a text message and expect the echoed response.
            var payload = Encoding.UTF8.GetBytes("hello");
            await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[1024];
            using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), receiveTimeout.Token);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            Assert.Equal("Echo: hello", Encoding.UTF8.GetString(buffer, 0, result.Count));

            // Close the connection; the server-side client must be unregistered.
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            await WaitUntilAsync(() => app.GetWebSocketClientCount() == 0, TimeSpan.FromSeconds(5));
            Assert.Equal(0, app.GetWebSocketClientCount());
            Assert.Empty(app.GetWebSocketClients());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseWebSocket_UpgradeHeaderCaseInsensitive_Returns101()
    {
        var (app, baseAddress) = await StartEchoServerAsync();
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, baseAddress);
            request.Headers.TryAddWithoutValidation("Upgrade", "WebSocket"); // mixed case on purpose
            request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
            request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
            request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            Assert.Equal(HttpStatusCode.SwitchingProtocols, response.StatusCode);
            Assert.Contains(response.Headers, h => h.Key.Equals("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseWebSocket_MissingSecWebSocketKey_Returns400()
    {
        var (app, baseAddress) = await StartEchoServerAsync();
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, baseAddress);
            request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
            request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
            request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void HttpUpgrade_MissingSecWebSocketKey_ThrowsHandshakeException()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Upgrade"] = "websocket";
        httpContext.Request.Headers["Connection"] = "Upgrade";
        httpContext.Request.Headers["Sec-WebSocket-Version"] = "13";

        var context = AspNetCoreHttpContext.GetHttpContext(app, httpContext, timeOut: 0);

        Assert.Throws<WebSocketHandshakeException>(() => context.HttpUpgrade());
    }

    [Fact]
    public void HttpUpgrade_UnsupportedVersion_ThrowsHandshakeException()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Upgrade"] = "websocket";
        httpContext.Request.Headers["Connection"] = "Upgrade";
        httpContext.Request.Headers["Sec-WebSocket-Key"] = Convert.ToBase64String(new byte[16]);
        httpContext.Request.Headers["Sec-WebSocket-Version"] = "12";

        var context = AspNetCoreHttpContext.GetHttpContext(app, httpContext, timeOut: 0);

        Assert.Throws<WebSocketHandshakeException>(() => context.HttpUpgrade());
    }
}
