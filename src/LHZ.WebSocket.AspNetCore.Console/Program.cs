using System;
using LHZ.WebSocket.AspNetCore;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseWebSocket(async (context) =>
{
    var webSocketClient = await context.HttpUpgradeAsync();
    webSocketClient.OnMessageReceived += (client, message) =>
    {
        Console.WriteLine($"Received message: {message}");
        client.SendMessage($"Echo: {message}");
    };
    webSocketClient.OnCloseRecived += (client, msg) =>
    {
        Console.WriteLine($"Client closed: {client.ID}");
        client.Close();
    };
});
app.Run();
