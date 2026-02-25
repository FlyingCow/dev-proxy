namespace DevProxy.Shared.Messages;

public class TunnelHttpResponseMessage : TunnelMessage
{
    public TunnelHttpResponseMessage()
    {
        Type = MessageType.Response;
    }

    public string RequestId { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public Dictionary<string, string[]> Headers { get; set; } = new();
    public byte[]? Body { get; set; }
    public string? ErrorMessage { get; set; }
}
