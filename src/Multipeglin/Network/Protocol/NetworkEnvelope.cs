namespace Multipeglin.Network.Protocol;

public class NetworkEnvelope
{
    public string TypeId { get; set; }
    public string Payload { get; set; }
    public long Sequence { get; set; }
    public long TimestampMs { get; set; }
}
