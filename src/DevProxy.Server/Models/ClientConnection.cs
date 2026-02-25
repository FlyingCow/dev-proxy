using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace DevProxy.Server.Models;

public class ClientConnection
{
    public string ClientId { get; set; } = string.Empty;
    public WebSocket WebSocket { get; set; } = null!;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource<byte[]>> PendingRequests { get; } = new();
}
