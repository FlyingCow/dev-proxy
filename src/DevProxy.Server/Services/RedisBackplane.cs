using System.Collections.Concurrent;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;
using StackExchange.Redis;

namespace DevProxy.Server.Services;

public class RedisBackplane : IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ISubscriber _subscriber;
    private readonly IDatabase _db;
    private readonly string _instanceId;
    private readonly ILogger<RedisBackplane> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
    private readonly ClientConnectionManager _connectionManager;

    private const string ClientRegistryKey = "devproxy:clients";
    private const string RequestChannel = "devproxy:requests";
    private const string ResponseChannel = "devproxy:responses";

    public bool IsEnabled { get; }

    public RedisBackplane(
        IConfiguration configuration,
        ClientConnectionManager connectionManager,
        ILogger<RedisBackplane> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _instanceId = Guid.NewGuid().ToString("N")[..8];

        var connectionString = configuration.GetConnectionString("Redis");
        IsEnabled = !string.IsNullOrEmpty(connectionString);

        if (IsEnabled)
        {
            _redis = ConnectionMultiplexer.Connect(connectionString!);
            _subscriber = _redis.GetSubscriber();
            _db = _redis.GetDatabase();

            SubscribeToChannels();
            _logger.LogInformation("Redis backplane enabled. Instance ID: {InstanceId}", _instanceId);
        }
        else
        {
            _redis = null!;
            _subscriber = null!;
            _db = null!;
            _logger.LogInformation("Redis backplane disabled. Running in single-instance mode.");
        }
    }

    private void SubscribeToChannels()
    {
        _subscriber.Subscribe(RedisChannel.Literal($"{RequestChannel}:{_instanceId}"), async (channel, message) =>
        {
            await HandleIncomingRequest(message!);
        });

        _subscriber.Subscribe(RedisChannel.Literal(ResponseChannel), (channel, message) =>
        {
            HandleIncomingResponse(message!);
        });
    }

    public async Task RegisterClientAsync(string clientId)
    {
        if (!IsEnabled) return;

        await _db.HashSetAsync(ClientRegistryKey, clientId, _instanceId);
        _logger.LogDebug("Registered client '{ClientId}' on instance '{InstanceId}'", clientId, _instanceId);
    }

    public async Task UnregisterClientAsync(string clientId)
    {
        if (!IsEnabled) return;

        await _db.HashDeleteAsync(ClientRegistryKey, clientId);
        _logger.LogDebug("Unregistered client '{ClientId}'", clientId);
    }

    public async Task<string?> GetClientInstanceAsync(string clientId)
    {
        if (!IsEnabled) return null;

        var instanceId = await _db.HashGetAsync(ClientRegistryKey, clientId);
        return instanceId.HasValue ? instanceId.ToString() : null;
    }

    public async Task<TunnelHttpResponseMessage?> ForwardRequestAsync(
        string clientId,
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled) return null;

        var targetInstance = await GetClientInstanceAsync(clientId);
        if (targetInstance == null)
        {
            _logger.LogWarning("Client '{ClientId}' not found in registry", clientId);
            return null;
        }

        if (targetInstance == _instanceId)
        {
            return null;
        }

        _logger.LogDebug("Forwarding request {RequestId} for client '{ClientId}' to instance '{TargetInstance}'",
            request.Id, clientId, targetInstance);

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;

        try
        {
            var envelope = new RequestEnvelope
            {
                ClientId = clientId,
                RequestId = request.Id,
                SourceInstance = _instanceId,
                Payload = MessageSerializer.SerializeToString(request)
            };

            var envelopeJson = System.Text.Json.JsonSerializer.Serialize(envelope);
            await _subscriber.PublishAsync(
                RedisChannel.Literal($"{RequestChannel}:{targetInstance}"),
                envelopeJson);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() => tcs.TrySetCanceled());

            var responseBytes = await tcs.Task;
            return MessageSerializer.Deserialize(responseBytes) as TunnelHttpResponseMessage;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Cross-instance request {RequestId} timed out", request.Id);
            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = 504,
                ErrorMessage = "Gateway timeout"
            };
        }
        finally
        {
            _pendingRequests.TryRemove(request.Id, out _);
        }
    }

    private async Task HandleIncomingRequest(string message)
    {
        try
        {
            var envelope = System.Text.Json.JsonSerializer.Deserialize<RequestEnvelope>(message);
            if (envelope == null) return;

            var request = MessageSerializer.Deserialize(envelope.Payload) as TunnelHttpRequestMessage;
            if (request == null) return;

            _logger.LogDebug("Received cross-instance request {RequestId} for client '{ClientId}'",
                envelope.RequestId, envelope.ClientId);

            if (!_connectionManager.TryGetConnection(envelope.ClientId, out var connection) || connection == null)
            {
                await SendErrorResponse(envelope, "Client not connected to this instance");
                return;
            }

            var requestForwarder = new RequestForwarder(_connectionManager,
                _logger as ILogger<RequestForwarder> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RequestForwarder>.Instance);

            var response = await requestForwarder.ForwardRequestAsync(
                envelope.ClientId,
                request,
                connection.CancellationTokenSource.Token);

            var responseEnvelope = new ResponseEnvelope
            {
                RequestId = envelope.RequestId,
                TargetInstance = envelope.SourceInstance,
                Payload = response != null ? MessageSerializer.SerializeToString(response) : null
            };

            var responseJson = System.Text.Json.JsonSerializer.Serialize(responseEnvelope);
            await _subscriber.PublishAsync(RedisChannel.Literal(ResponseChannel), responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling cross-instance request");
        }
    }

    private async Task SendErrorResponse(RequestEnvelope envelope, string errorMessage)
    {
        var response = new TunnelHttpResponseMessage
        {
            RequestId = envelope.RequestId,
            StatusCode = 502,
            ErrorMessage = errorMessage
        };

        var responseEnvelope = new ResponseEnvelope
        {
            RequestId = envelope.RequestId,
            TargetInstance = envelope.SourceInstance,
            Payload = MessageSerializer.SerializeToString(response)
        };

        var responseJson = System.Text.Json.JsonSerializer.Serialize(responseEnvelope);
        await _subscriber.PublishAsync(RedisChannel.Literal(ResponseChannel), responseJson);
    }

    private void HandleIncomingResponse(string message)
    {
        try
        {
            var envelope = System.Text.Json.JsonSerializer.Deserialize<ResponseEnvelope>(message);
            if (envelope == null || envelope.TargetInstance != _instanceId) return;

            if (_pendingRequests.TryGetValue(envelope.RequestId, out var tcs))
            {
                if (envelope.Payload != null)
                {
                    tcs.TrySetResult(System.Text.Encoding.UTF8.GetBytes(envelope.Payload));
                }
                else
                {
                    tcs.TrySetCanceled();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling cross-instance response");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (IsEnabled)
        {
            await _subscriber.UnsubscribeAllAsync();
            await _redis.CloseAsync();
        }
    }

    private class RequestEnvelope
    {
        public string ClientId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string SourceInstance { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    private class ResponseEnvelope
    {
        public string RequestId { get; set; } = string.Empty;
        public string TargetInstance { get; set; } = string.Empty;
        public string? Payload { get; set; }
    }
}
