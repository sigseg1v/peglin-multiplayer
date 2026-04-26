using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class OrbDiscardedServerHandler : IServerHandler<OrbDiscardedEvent>
{
    public OrbDiscardedEvent Handle(OrbDiscardedEvent networkEvent) => networkEvent;
}
