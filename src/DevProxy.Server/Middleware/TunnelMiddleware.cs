using DevProxy.Server.Services;
using DevProxy.Shared.Messages;

namespace DevProxy.Server.Middleware;

public class TunnelMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TunnelMiddleware> _logger;

    public TunnelMiddleware(RequestDelegate next, ILogger<TunnelMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ClientConnectionManager connectionManager,
        RequestForwarder requestForwarder)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/ws") || path.StartsWith("/health"))
        {
            await _next(context);
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            await _next(context);
            return;
        }

        var clientId = segments[0];

        if (!connectionManager.TryGetConnection(clientId, out _))
        {
            await _next(context);
            return;
        }

        var forwardPath = "/" + string.Join("/", segments.Skip(1));

        _logger.LogDebug("Tunneling request for client '{ClientId}': {Method} {Path}",
            clientId, context.Request.Method, forwardPath);

        var headers = new Dictionary<string, string[]>();
        foreach (var (key, values) in context.Request.Headers)
        {
            if (!IsHopByHopHeader(key))
            {
                headers[key] = values.ToArray();
            }
        }

        byte[]? body = null;
        if (context.Request.ContentLength > 0)
        {
            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms);
            body = ms.ToArray();
        }

        var request = new TunnelHttpRequestMessage
        {
            Path = forwardPath,
            Method = context.Request.Method,
            QueryString = context.Request.QueryString.Value,
            Headers = headers,
            Body = body,
            IsOutbound = false
        };

        var response = await requestForwarder.ForwardRequestAsync(clientId, request, context.RequestAborted);

        if (response == null)
        {
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync("Client not available");
            return;
        }

        context.Response.StatusCode = response.StatusCode;

        foreach (var (key, values) in response.Headers)
        {
            if (!IsHopByHopHeader(key))
            {
                context.Response.Headers[key] = values;
            }
        }

        if (response.Body != null && response.Body.Length > 0)
        {
            await context.Response.Body.WriteAsync(response.Body);
        }
    }

    private static bool IsHopByHopHeader(string headerName)
    {
        return headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
    }
}

public static class TunnelMiddlewareExtensions
{
    public static IApplicationBuilder UseTunnelMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TunnelMiddleware>();
    }
}
