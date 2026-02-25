namespace DevProxy.Shared.Messages;

public class TunnelHttpRequestMessage : TunnelMessage
{
    public TunnelHttpRequestMessage()
    {
        Type = MessageType.Request;
    }

    public string? Url { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string? QueryString { get; set; }
    public Dictionary<string, string[]> Headers { get; set; } = new();
    public byte[]? Body { get; set; }
    public bool IsOutbound { get; set; }
}
