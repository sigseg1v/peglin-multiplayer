using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// No-op pass-through. HostPongEvent only travels client → host, so the host
/// never Dispatch()es it. The real per-peer RTT update happens in
/// HostPongClientHandler (which fires on the receiving side per registry
/// convention).
/// </summary>
public sealed class HostPongServerHandler : IServerHandler<HostPongEvent>
{
    public HostPongEvent Handle(HostPongEvent networkEvent) => null;
}
