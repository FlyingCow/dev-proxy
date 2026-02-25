using DevProxy.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace DevProxy.Client.Services;

public class LocalRequestHandler
{
    private readonly HttpClient _httpClient;
    private readonly string _localUrl;
    private readonly ILogger<LocalRequestHandler> _logger;

    public LocalRequestHandler(string localUrl, ILogger<LocalRequestHandler> logger)
    {
        _localUrl = localUrl.TrimEnd('/');
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<TunnelHttpResponseMessage> HandleRequestAsync(
        TunnelHttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = _localUrl + request.Path;
        if (!string.IsNullOrEmpty(request.QueryString))
        {
            url += request.QueryString;
        }

        _logger.LogDebug("Forwarding {Method} {Path} to {Url}", request.Method, request.Path, url);

        try
        {
            using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), url);

            foreach (var (key, values) in request.Headers)
            {
                if (IsContentHeader(key))
                {
                    continue;
                }
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
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

            _logger.LogDebug("Local service responded with {StatusCode}", (int)httpResponse.StatusCode);

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
            _logger.LogError(ex, "Error forwarding request to local service");
            return new TunnelHttpResponseMessage
            {
                RequestId = request.Id,
                StatusCode = 502,
                ErrorMessage = $"Local service error: {ex.Message}"
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
