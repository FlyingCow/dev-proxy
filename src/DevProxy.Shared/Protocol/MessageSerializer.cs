using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevProxy.Shared.Messages;

namespace DevProxy.Shared.Protocol;

public static class MessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static byte[] Serialize(TunnelMessage message)
    {
        var json = JsonSerializer.Serialize(message, Options);
        return Encoding.UTF8.GetBytes(json);
    }

    public static string SerializeToString(TunnelMessage message)
    {
        return JsonSerializer.Serialize(message, Options);
    }

    public static TunnelMessage? Deserialize(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return Deserialize(json);
    }

    public static TunnelMessage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<TunnelMessage>(json, Options);
    }

    public static T? Deserialize<T>(string json) where T : TunnelMessage
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
