using DevProxy.Server.Middleware;
using DevProxy.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<ClientConnectionManager>();
builder.Services.AddSingleton<RedisBackplane>();
builder.Services.AddSingleton<PeerBackplane>();
builder.Services.AddScoped<RequestForwarder>();
builder.Services.AddScoped<OutboundProxyService>();
builder.Services.AddHttpClient("OutboundProxy");
builder.Services.AddHttpClient("PeerBackplane");

// Support both IIS and Windows Service hosting
builder.Host.UseWindowsService();

var app = builder.Build();

// Enable WebSockets (required for IIS - must be enabled in IIS features)
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.UseTunnelMiddleware();
app.MapControllers();

app.Run();
