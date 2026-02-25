using System.Net.WebSockets;
using DevProxy.Server.Models;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;

namespace DevProxy.Server.Services;

public class RequestForwarder
{
    private readonly ClientConnectionManager _connectionManager;
    private readonly ILogger<RequestForwarder> _logger;
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(60);

    public RequestForwarder(ClientConnectionManager connectionManager, ILogger<RequestForwarder> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<TunnelHttpResponseMessage?> ForwardRequestAsync(
        string clientId,
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_connectionManager.TryGetConnection(clientId, out var connection) || connection == null)
        {
            _logger.LogWarning("Client '{ClientId}' not found for request forwarding", clientId);
            return null;
        }

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PendingRequests[request.Id] = tcs;

        try
        {
            var messageBytes = MessageSerializer.Serialize(request);
            await connection.WebSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);

            _logger.LogDebug("Forwarded request {RequestId} to client '{ClientId}'", request.Id, clientId);

            using var timeoutCts = new CancellationTokenSource(_requestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() => tcs.TrySetCanceled());

            var responseBytes = await tcs.Task;
            var response = MessageSerializer.Deserialize(responseBytes) as TunnelHttpResponseMessage;

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Request {RequestId} to client '{ClientId}' timed out", request.Id, clientId);
            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = 504,
                ErrorMessage = "Gateway timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding request {RequestId} to client '{ClientId}'", request.Id, clientId);
            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = 502,
                ErrorMessage = "Bad gateway"
            };
        }
        finally
        {
            connection.PendingRequests.TryRemove(request.Id, out _);
        }
    }
}
