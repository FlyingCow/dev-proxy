using System.Collections.Concurrent;
using System.Net.WebSockets;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace DevProxy.Client.Services;

public class OutboundRequestService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TunnelHttpResponseMessage>> _pendingRequests = new();
    private ClientWebSocket? _webSocket;
    private readonly ILogger<OutboundRequestService> _logger;

    public OutboundRequestService(ILogger<OutboundRequestService> logger)
    {
        _logger = logger;
    }

    public void SetWebSocket(ClientWebSocket webSocket)
    {
        _webSocket = webSocket;
    }

    public async Task<TunnelHttpResponseMessage?> SendOutboundRequestAsync(
        string url,
        string method,
        Dictionary<string, string[]>? headers,
        byte[]? body,
        CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send outbound request - not connected");
            return null;
        }

        var request = new TunnelHttpRequestMessage
        {
            Url = url,
            Method = method,
            Headers = headers ?? new Dictionary<string, string[]>(),
            Body = body,
            IsOutbound = true
        };

        var tcs = new TaskCompletionSource<TunnelHttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;

        try
        {
            var bytes = MessageSerializer.Serialize(request);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Outbound request to {Url} timed out", url);
            return null;
        }
        finally
        {
            _pendingRequests.TryRemove(request.Id, out _);
        }
    }

    public void HandleResponse(TunnelHttpResponseMessage response)
    {
        if (_pendingRequests.TryGetValue(response.RequestId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }
}
