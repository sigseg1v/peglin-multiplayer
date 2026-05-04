namespace Multipeglin.Events.Network;

/// <summary>
/// Client → host echo of a HostPingEvent. The host matches the token against
/// its outstanding-probes table to derive RTT to that specific peer.
/// </summary>
public class HostPongEvent
{
    public long Token { get; set; }
}
