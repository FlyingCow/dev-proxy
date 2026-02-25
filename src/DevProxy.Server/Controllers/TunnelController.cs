using System.Net.WebSockets;
using DevProxy.Server.Models;
using DevProxy.Server.Services;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace DevProxy.Server.Controllers;

[ApiController]
[Route("ws")]
public class TunnelController : ControllerBase
{
    private readonly ClientConnectionManager _connectionManager;
    private readonly OutboundProxyService _outboundProxyService;
    private readonly ILogger<TunnelController> _logger;

    public TunnelController(
        ClientConnectionManager connectionManager,
        OutboundProxyService outboundProxyService,
        ILogger<TunnelController> logger)
    {
        _connectionManager = connectionManager;
        _outboundProxyService = outboundProxyService;
        _logger = logger;
    }

    [HttpGet]
    public async Task Connect()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            await HttpContext.Response.WriteAsync("WebSocket connection required");
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await HandleWebSocketConnectionAsync(webSocket);
    }

    private async Task HandleWebSocketConnectionAsync(WebSocket webSocket)
    {
        ClientConnection? connection = null;
        string? clientId = null;

        try
        {
            var registrationMessage = await ReceiveMessageAsync(webSocket);
            if (registrationMessage is not ControlMessage controlMsg || controlMsg.Action != ControlAction.Register)
            {
                _logger.LogWarning("First message must be a registration");
                await SendErrorAndCloseAsync(webSocket, "First message must be a registration");
                return;
            }

            clientId = controlMsg.ClientId;
            if (string.IsNullOrEmpty(clientId))
            {
                await SendErrorAndCloseAsync(webSocket, "Client ID is required");
                return;
            }

            if (!_connectionManager.TryRegister(clientId, webSocket, out connection))
            {
                await SendErrorAndCloseAsync(webSocket, $"Client ID '{clientId}' is already in use");
                return;
            }

            var registeredResponse = new ControlMessage
            {
                Action = ControlAction.Registered,
                ClientId = clientId,
                Message = "Successfully registered"
            };
            await SendMessageAsync(webSocket, registeredResponse);

            _logger.LogInformation("Client '{ClientId}' connected from {RemoteIp}",
                clientId, HttpContext.Connection.RemoteIpAddress);

            await ProcessMessagesAsync(webSocket, connection);
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "WebSocket error for client '{ClientId}'", clientId ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket connection for client '{ClientId}'", clientId ?? "unknown");
        }
        finally
        {
            if (clientId != null)
            {
                _connectionManager.Unregister(clientId);
                _logger.LogInformation("Client '{ClientId}' disconnected", clientId);
            }

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
    }

    private async Task ProcessMessagesAsync(WebSocket webSocket, ClientConnection connection)
    {
        var buffer = new byte[64 * 1024];

        while (webSocket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(webSocket, connection.CancellationTokenSource.Token);
            if (message == null)
            {
                break;
            }

            switch (message)
            {
                case ControlMessage controlMsg when controlMsg.Action == ControlAction.Heartbeat:
                    connection.LastHeartbeat = DateTimeOffset.UtcNow;
                    var ackMessage = new ControlMessage
                    {
                        Action = ControlAction.HeartbeatAck,
                        ClientId = connection.ClientId
                    };
                    await SendMessageAsync(webSocket, ackMessage);
                    break;

                case TunnelHttpResponseMessage responseMsg:
                    if (connection.PendingRequests.TryGetValue(responseMsg.RequestId, out var tcs))
                    {
                        tcs.TrySetResult(MessageSerializer.Serialize(responseMsg));
                    }
                    else
                    {
                        _logger.LogWarning("Received response for unknown request {RequestId}", responseMsg.RequestId);
                    }
                    break;

                case TunnelHttpRequestMessage requestMsg when requestMsg.IsOutbound:
                    var response = await _outboundProxyService.ExecuteOutboundRequestAsync(
                        requestMsg, connection.CancellationTokenSource.Token);
                    await SendMessageAsync(webSocket, response);
                    break;

                default:
                    _logger.LogWarning("Received unexpected message type: {Type}", message.GetType().Name);
                    break;
            }
        }
    }

    private async Task<TunnelMessage?> ReceiveMessageAsync(WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return MessageSerializer.Deserialize(ms.ToArray());
    }

    private static async Task SendMessageAsync(WebSocket webSocket, TunnelMessage message)
    {
        var bytes = MessageSerializer.Serialize(message);
        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private static async Task SendErrorAndCloseAsync(WebSocket webSocket, string errorMessage)
    {
        var error = new ControlMessage
        {
            Action = ControlAction.Error,
            Message = errorMessage
        };
        await SendMessageAsync(webSocket, error);
        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, errorMessage, CancellationToken.None);
    }
}
