using DevProxy.Server.Services;
using DevProxy.Shared.Messages;
using DevProxy.Shared.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace DevProxy.Server.Controllers;

[ApiController]
[Route("_internal")]
public class InternalController : ControllerBase
{
    private readonly ClientConnectionManager _connectionManager;
    private readonly RequestForwarder _requestForwarder;
    private readonly ILogger<InternalController> _logger;

    public InternalController(
        ClientConnectionManager connectionManager,
        RequestForwarder requestForwarder,
        ILogger<InternalController> logger)
    {
        _connectionManager = connectionManager;
        _requestForwarder = requestForwarder;
        _logger = logger;
    }

    [HttpGet("clients/{clientId}")]
    public IActionResult CheckClient(string clientId)
    {
        if (_connectionManager.TryGetConnection(clientId, out _))
        {
            return Ok(new { found = true, clientId });
        }
        return NotFound();
    }

    [HttpGet("clients")]
    public IActionResult ListClients()
    {
        var clients = _connectionManager.GetAllConnections()
            .Select(c => new
            {
                c.ClientId,
                c.ConnectedAt,
                c.LastHeartbeat
            });
        return Ok(clients);
    }

    [HttpPost("forward/{clientId}")]
    public async Task<IActionResult> ForwardToClient(string clientId, [FromBody] ForwardRequest request)
    {
        if (!_connectionManager.TryGetConnection(clientId, out _))
        {
            return NotFound(new { error = "Client not found on this instance" });
        }

        var tunnelRequest = MessageSerializer.Deserialize(request.RequestPayload) as TunnelHttpRequestMessage;
        if (tunnelRequest == null)
        {
            return BadRequest(new { error = "Invalid request payload" });
        }

        _logger.LogDebug("Received peer forward request for client '{ClientId}'", clientId);

        var response = await _requestForwarder.ForwardRequestAsync(
            clientId,
            tunnelRequest,
            HttpContext.RequestAborted);

        if (response == null)
        {
            return StatusCode(502, new { error = "Failed to forward to client" });
        }

        return Ok(new ForwardResponse
        {
            ResponsePayload = MessageSerializer.SerializeToString(response)
        });
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
