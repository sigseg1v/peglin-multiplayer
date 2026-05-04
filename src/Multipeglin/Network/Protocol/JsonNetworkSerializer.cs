using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Multipeglin.Network.Protocol;

public class JsonNetworkSerializer : INetworkSerializer
{
    // 1-byte framing header. Lets us upgrade compression algorithms later
    // without another protocol break: any unknown header byte is rejected.
    private const byte FrameRawJson = 0x00;
    private const byte FrameDeflateJson = 0x01;

    // Below this raw size, deflate's ~6-byte block overhead + worse worst-case
    // means the framed message is larger than the unframed one. Skip it.
    private const int CompressionThresholdBytes = 256;

    private readonly MessageTypeRegistry _typeRegistry;
    private long _sequence;

    public JsonNetworkSerializer(MessageTypeRegistry typeRegistry)
    {
        _typeRegistry = typeRegistry;
    }

    public byte[] Serialize<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class
    {
        var typeId = _typeRegistry.GetTypeId<TNetworkEvent>();
        var payload = JsonConvert.SerializeObject(networkEvent);

        var envelope = new NetworkEnvelope
        {
            TypeId = typeId,
            Payload = payload,
            Sequence = Interlocked.Increment(ref _sequence),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonConvert.SerializeObject(envelope);
        var raw = Encoding.UTF8.GetBytes(json);
        return Frame(raw);
    }

    public (string typeId, string jsonPayload) Deserialize(byte[] data)
    {
        var json = Unframe(data);
        var envelope = JsonConvert.DeserializeObject<NetworkEnvelope>(json);
        if (envelope == null || string.IsNullOrEmpty(envelope.TypeId))
        {
            throw new InvalidOperationException($"Malformed network envelope (json length={json.Length})");
        }

        return (envelope.TypeId, envelope.Payload);
    }

    private static byte[] Frame(byte[] raw)
    {
        if (raw.Length < CompressionThresholdBytes)
        {
            var framed = new byte[raw.Length + 1];
            framed[0] = FrameRawJson;
            Buffer.BlockCopy(raw, 0, framed, 1, raw.Length);
            return framed;
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            compressed = ms.ToArray();
        }

        // If compression somehow grew the payload (very high entropy data),
        // fall back to raw — paying 1 header byte beats a larger frame.
        if (compressed.Length + 1 >= raw.Length + 1)
        {
            var framed = new byte[raw.Length + 1];
            framed[0] = FrameRawJson;
            Buffer.BlockCopy(raw, 0, framed, 1, raw.Length);
            return framed;
        }

        var result = new byte[compressed.Length + 1];
        result[0] = FrameDeflateJson;
        Buffer.BlockCopy(compressed, 0, result, 1, compressed.Length);
        return result;
    }

    private static string Unframe(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            throw new InvalidOperationException("Empty network frame");
        }

        switch (data[0])
        {
            case FrameRawJson:
                return Encoding.UTF8.GetString(data, 1, data.Length - 1);

            case FrameDeflateJson:
                using (var ms = new MemoryStream(data, 1, data.Length - 1))
                using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
                using (var reader = new StreamReader(deflate, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }

            default:
                throw new InvalidOperationException(
                    $"Unknown network frame header 0x{data[0]:X2} (length={data.Length})");
        }
    }
}
