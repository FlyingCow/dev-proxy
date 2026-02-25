using System.Collections.Concurrent;
using System.Net.WebSockets;
using DevProxy.Server.Models;

namespace DevProxy.Server.Services;

public class ClientConnectionManager
{
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private readonly ILogger<ClientConnectionManager> _logger;

    public ClientConnectionManager(ILogger<ClientConnectionManager> logger)
    {
        _logger = logger;
    }

    public bool TryRegister(string clientId, WebSocket webSocket, out ClientConnection? connection)
    {
        connection = new ClientConnection
        {
            ClientId = clientId,
            WebSocket = webSocket,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        if (_connections.TryAdd(clientId, connection))
        {
            _logger.LogInformation("Client '{ClientId}' registered", clientId);
            return true;
        }

        _logger.LogWarning("Client '{ClientId}' already registered", clientId);
        connection = null;
        return false;
    }

    public bool TryGetConnection(string clientId, out ClientConnection? connection)
    {
        return _connections.TryGetValue(clientId, out connection);
    }

    public void Unregister(string clientId)
    {
        if (_connections.TryRemove(clientId, out var connection))
        {
            connection.CancellationTokenSource.Cancel();
            _logger.LogInformation("Client '{ClientId}' unregistered", clientId);
        }
    }

    public IEnumerable<ClientConnection> GetAllConnections()
    {
        return _connections.Values;
    }

    public int ConnectionCount => _connections.Count;
}
