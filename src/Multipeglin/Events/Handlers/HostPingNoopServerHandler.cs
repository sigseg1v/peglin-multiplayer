using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>No-op — host never receives HostPingEvent (host originates it).</summary>
public sealed class HostPingNoopServerHandler : IServerHandler<HostPingEvent>
{
    public HostPingEvent Handle(HostPingEvent networkEvent) => null;
}
