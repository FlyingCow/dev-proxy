using System.Net.WebSockets;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace DevProxy.Client.Services;

public class TunnelClient : IAsyncDisposable
{
    private readonly string _serverUrl;
    private readonly string _clientId;
    private readonly LocalRequestHandler _localRequestHandler;
    private readonly ILogger<TunnelClient> _logger;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _isConnected;

    public event Action<string>? OnConnected;
    public event Action<string>? OnDisconnected;
    public event Action<string>? OnError;

    public TunnelClient(
        string serverUrl,
        string clientId,
        LocalRequestHandler localRequestHandler,
        ILogger<TunnelClient> logger)
    {
        _serverUrl = serverUrl;
        _clientId = clientId;
        _localRequestHandler = localRequestHandler;
        _logger = logger;
    }

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectInternalAsync(_cts.Token);
                _receiveTask = ReceiveLoopAsync(_cts.Token);
                await _receiveTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection error, reconnecting...");
                OnError?.Invoke($"Connection error: {ex.Message}");
                _isConnected = false;

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task ConnectInternalAsync(CancellationToken cancellationToken)
    {
        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

        var wsUrl = _serverUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        wsUrl = wsUrl.TrimEnd('/') + "/ws";

        _logger.LogInformation("Connecting to {Url}...", wsUrl);
        await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);

        var registerMessage = new ControlMessage
        {
            Action = ControlAction.Register,
            ClientId = _clientId
        };
        await SendMessageAsync(registerMessage, cancellationToken);

        var response = await ReceiveMessageAsync(cancellationToken);
        if (response is ControlMessage controlMsg)
        {
            if (controlMsg.Action == ControlAction.Registered)
            {
                _isConnected = true;
                _logger.LogInformation("Successfully registered as '{ClientId}'", _clientId);
                OnConnected?.Invoke(_clientId);
            }
            else if (controlMsg.Action == ControlAction.Error)
            {
                throw new InvalidOperationException($"Registration failed: {controlMsg.Message}");
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var heartbeatTask = HeartbeatLoopAsync(cancellationToken);

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(cancellationToken);
                if (message == null)
                {
                    break;
                }

                _ = ProcessMessageAsync(message, cancellationToken);
            }
        }
        finally
        {
            _isConnected = false;
            OnDisconnected?.Invoke(_clientId);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                var heartbeat = new ControlMessage
                {
                    Action = ControlAction.Heartbeat,
                    ClientId = _clientId
                };
                await SendMessageAsync(heartbeat, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
                break;
            }
        }
    }

    private async Task ProcessMessageAsync(TunnelMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message)
            {
                case TunnelHttpRequestMessage request when !request.IsOutbound:
                    var response = await _localRequestHandler.HandleRequestAsync(request, cancellationToken);
                    await SendMessageAsync(response, cancellationToken);
                    break;

                case ControlMessage control when control.Action == ControlAction.HeartbeatAck:
                    _logger.LogDebug("Heartbeat acknowledged");
                    break;

                default:
                    _logger.LogWarning("Received unexpected message type: {Type}", message.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
        }
    }

    private async Task<TunnelMessage?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null)
            return null;

        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return MessageSerializer.Deserialize(ms.ToArray());
    }

    private async Task SendMessageAsync(TunnelMessage message, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var bytes = MessageSerializer.Serialize(message);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch { }
        }

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
        }

        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}
