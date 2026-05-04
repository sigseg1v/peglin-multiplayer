using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>No-op — clients never receive HostPongEvent (they originate it).</summary>
public sealed class HostPongNoopClientHandler : IClientHandler<HostPongEvent>
{
    public void Handle(HostPongEvent networkEvent) { }
}
