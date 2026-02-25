using DevProxy.Shared.Messages;

namespace DevProxy.Server.Services;

public class OutboundProxyService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OutboundProxyService> _logger;

    public OutboundProxyService(IHttpClientFactory httpClientFactory, ILogger<OutboundProxyService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OutboundProxy");
        _logger = logger;
    }

    public async Task<TunnelHttpResponseMessage> ExecuteOutboundRequestAsync(
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Url))
        {
            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = 400,
                ErrorMessage = "URL is required for outbound requests"
            };
        }

        try
        {
            _logger.LogDebug("Executing outbound request to {Url}", request.Url);

            using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

            foreach (var (key, values) in request.Headers)
            {
                if (IsContentHeader(key))
                {
                    continue;
                }
                httpRequest.Headers.TryAddWithoutValidation(key, values);
            }

            if (request.Body != null && request.Body.Length > 0)
            {
                httpRequest.Content = new ByteArrayContent(request.Body);
                foreach (var (key, values) in request.Headers)
                {
                    if (IsContentHeader(key))
                    {
                        httpRequest.Content.Headers.TryAddWithoutValidation(key, values);
                    }
                }
            }

            using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

            var responseHeaders = new Dictionary<string, string[]>();
            foreach (var (key, values) in httpResponse.Headers)
            {
                responseHeaders[key] = values.ToArray();
            }
            foreach (var (key, values) in httpResponse.Content.Headers)
            {
                responseHeaders[key] = values.ToArray();
            }

            var responseBody = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken);

            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = (int)httpResponse.StatusCode,
                Headers = responseHeaders,
                Body = responseBody
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing outbound request to {Url}", request.Url);
            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = 502,
                ErrorMessage = $"Outbound request failed: {ex.Message}"
            };
        }
    }

    private static bool IsContentHeader(string headerName)
    {
        return headerName.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Allow", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Expires", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase);
    }
}
