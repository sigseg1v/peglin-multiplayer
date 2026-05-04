using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>No-op — clients never receive PingEvent (ping flows client → host only).</summary>
public sealed class PingNoopClientHandler : IClientHandler<PingEvent>
{
    public void Handle(PingEvent networkEvent) { }
}
