namespace DevProxy.Shared.Messages;

public enum ControlAction
{
    Register,
    Registered,
    Heartbeat,
    HeartbeatAck,
    Disconnect,
    Error
}

public class ControlMessage : TunnelMessage
{
    public ControlMessage()
    {
        Type = MessageType.Register;
    }

    public ControlAction Action { get; set; }
    public string? ClientId { get; set; }
    public string? Message { get; set; }
}
