namespace Multipeglin.Events.Network;

/// <summary>
/// Host → client echo of a previous PingEvent. Token matches the client's
/// original probe so the client can ignore stale or out-of-order pongs.
/// </summary>
public class PongEvent
{
    public long Token { get; set; }
}
