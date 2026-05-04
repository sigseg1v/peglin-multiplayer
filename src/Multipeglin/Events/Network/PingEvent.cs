namespace Multipeglin.Events.Network;

/// <summary>
/// Client → host RTT probe. Host echoes the same Token back via PongEvent;
/// client diffs send-time vs receive-time to derive round-trip latency.
/// Used by AppLevelRttProvider to drive adaptive interpolation buffers
/// over transports that don't expose native ping (e.g. Steam P2P).
/// </summary>
public class PingEvent
{
    public long Token { get; set; }
}
