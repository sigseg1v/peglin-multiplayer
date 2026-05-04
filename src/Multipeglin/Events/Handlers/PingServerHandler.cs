using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// No-op pass-through. PingEvent only travels client → host, so the host
/// never Dispatch()es it. The actual echo (Ping → Pong) happens in
/// PingClientHandler, which fires on the receiving side per registry
/// convention.
/// </summary>
public sealed class PingServerHandler : IServerHandler<PingEvent>
{
    public PingEvent Handle(PingEvent networkEvent) => null;
}
