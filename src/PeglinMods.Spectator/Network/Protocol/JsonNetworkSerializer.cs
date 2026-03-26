using System;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PeglinMods.Spectator.Network.Protocol;

public class JsonNetworkSerializer : INetworkSerializer
{
    private readonly MessageTypeRegistry _typeRegistry;
    private long _sequence;

    public JsonNetworkSerializer(MessageTypeRegistry typeRegistry)
    {
        _typeRegistry = typeRegistry;
    }

    public byte[] Serialize<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class
    {
        var typeId = _typeRegistry.GetTypeId<TNetworkEvent>();
        var payload = JsonSerializer.Serialize(networkEvent);

        var envelope = new NetworkEnvelope
        {
            TypeId = typeId,
            Payload = payload,
            Sequence = Interlocked.Increment(ref _sequence),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(envelope);
        return Encoding.UTF8.GetBytes(json);
    }

    public (string typeId, string jsonPayload) Deserialize(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        var envelope = JsonSerializer.Deserialize<NetworkEnvelope>(json);
        return (envelope.TypeId, envelope.Payload);
    }
}
