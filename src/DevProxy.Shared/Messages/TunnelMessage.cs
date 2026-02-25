using System.Text.Json.Serialization;

namespace DevProxy.Shared.Messages;

public enum MessageType
{
    Request,
    Response,
    Register,
    Heartbeat,
    Error
}

[JsonDerivedType(typeof(TunnelHttpRequestMessage), "request")]
[JsonDerivedType(typeof(TunnelHttpResponseMessage), "response")]
[JsonDerivedType(typeof(ControlMessage), "control")]
public abstract class TunnelMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageType Type { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
