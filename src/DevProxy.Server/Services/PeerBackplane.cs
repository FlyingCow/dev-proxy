using System.Collections.Concurrent;
using System.Net.Http.Json;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;

namespace DevProxy.Server.Services;

public class PeerBackplane : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string[] _peerUrls;
    private readonly string _instanceId;
    private readonly ILogger<PeerBackplane> _logger;
    private readonly ClientConnectionManager _connectionManager;
    private readonly ConcurrentDictionary<string, string> _clientLocationCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

    public bool IsEnabled { get; }

    public PeerBackplane(
        IConfiguration configuration,
        ClientConnectionManager connectionManager,
        IHttpClientFactory httpClientFactory,
        ILogger<PeerBackplane> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _instanceId = Guid.NewGuid().ToString("N")[..8];
        _httpClient = httpClientFactory.CreateClient("PeerBackplane");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        var peersConfig = configuration.GetSection("Peers").Get<string[]>();
        _peerUrls = peersConfig ?? Array.Empty<string>();
        IsEnabled = _peerUrls.Length > 0;

        if (IsEnabled)
        {
            _logger.LogInformation("Peer backplane enabled with {Count} peers. Instance ID: {InstanceId}",
                _peerUrls.Length, _instanceId);
        }
        else
        {
            _logger.LogInformation("Peer backplane disabled. Running in single-instance mode.");
        }
    }

    public string InstanceId => _instanceId;

    public async Task<TunnelHttpResponseMessage?> ForwardRequestAsync(
        string clientId,
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled) return null;

        // Check cache first
        if (_clientLocationCache.TryGetValue(clientId, out var cachedPeerUrl))
        {
            var cachedResponse = await TryForwardToPeerAsync(cachedPeerUrl, clientId, request, cancellationToken);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }
            // Cache miss - remove stale entry
            _clientLocationCache.TryRemove(clientId, out _);
        }

        // Query all peers in parallel
        var tasks = _peerUrls.Select(peerUrl =>
            QueryPeerForClientAsync(peerUrl, clientId, request, cancellationToken));

        var results = await Task.WhenAll(tasks);
        return results.FirstOrDefault(r => r != null);
    }

    private async Task<TunnelHttpResponseMessage?> QueryPeerForClientAsync(
        string peerUrl,
        string clientId,
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            // First check if peer has the client
            var hasClientUrl = $"{peerUrl.TrimEnd('/')}/_internal/clients/{clientId}";
            var checkResponse = await _httpClient.GetAsync(hasClientUrl, cancellationToken);

            if (!checkResponse.IsSuccessStatusCode)
            {
                return null;
            }

            // Peer has the client - cache and forward
            _clientLocationCache[clientId] = peerUrl;
            return await TryForwardToPeerAsync(peerUrl, clientId, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query peer {PeerUrl} for client {ClientId}", peerUrl, clientId);
            return null;
        }
    }

    private async Task<TunnelHttpResponseMessage?> TryForwardToPeerAsync(
        string peerUrl,
        string clientId,
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var forwardUrl = $"{peerUrl.TrimEnd('/')}/_internal/forward/{clientId}";
            var payload = MessageSerializer.SerializeToString(request);

            var httpResponse = await _httpClient.PostAsJsonAsync(forwardUrl, new ForwardRequest
            {
                RequestPayload = payload
            }, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var responseWrapper = await httpResponse.Content.ReadFromJsonAsync<ForwardResponse>(cancellationToken);
            if (responseWrapper?.ResponsePayload == null)
            {
                return null;
            }

            return MessageSerializer.Deserialize(responseWrapper.ResponsePayload) as TunnelHttpResponseMessage;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to forward request to peer {PeerUrl}", peerUrl);
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    public class ForwardRequest
    {
        public string RequestPayload { get; set; } = string.Empty;
    }

    public class ForwardResponse
    {
        public string? ResponsePayload { get; set; }
    }
}
