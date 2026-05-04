namespace Multipeglin.Events.Network;

/// <summary>
/// Host → specific client probe. Mirror direction of PingEvent. The receiving
/// client echoes the token back as HostPongEvent so the host can compute
/// per-peer RTT (the per-client AppLevelRttProvider only gives the client
/// its own number).
/// </summary>
public class HostPingEvent
{
    public long Token { get; set; }
}
