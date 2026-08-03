using System.Threading.Tasks;
using LHZ.WebSocket.AspNetCore;
using AspNetCoreHttpContext = LHZ.WebSocket.AspNetCore.Http.HttpContext;
using LHZ.WebSocket.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LHZ.WebSocket.AspNetCore.Test;

public class WebSocketBuilderExtensionsTests
{
    [Fact]
    public void GetWebSocketClientCount_WithoutClients_ReturnsZero()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        Assert.Equal(0, app.GetWebSocketClientCount());
    }

    [Fact]
    public void GetWebSocketClients_WithoutClients_ReturnsEmpty()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        Assert.Empty(app.GetWebSocketClients());
    }

    [Fact]
    public void UseWebSocket_ReturnsSameApplicationBuilder()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        var result = app.UseWebSocket(context => context.Dispose());

        Assert.Same(app, result);
    }

    [Fact]
    public async Task UseWebSocket_NoUpgrade_CallsNextMiddleware()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        app.UseWebSocket(context => context.Dispose());
        app.Run(async context =>
        {
            context.Response.StatusCode = 204;
            await Task.CompletedTask;
        });

        var pipeline = app.Build();
        var httpContext = new DefaultHttpContext();

        await pipeline(httpContext);

        Assert.Equal(204, httpContext.Response.StatusCode);
    }
}

public class AspNetCoreHttpContextTests
{
    [Fact]
    public void GetHttpContext_InitializesAndDisposes()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        var httpContext = new DefaultHttpContext();

        var context = AspNetCoreHttpContext.GetHttpContext(app, httpContext, timeOut: 1);

        Assert.NotNull(context);
        Assert.Equal(HttpContextStatus.Initialized, context.Status);

        context.Dispose();

        Assert.Equal(HttpContextStatus.Rejected, context.Status);
    }
}
