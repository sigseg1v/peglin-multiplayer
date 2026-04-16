namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class OrbDiscardedServerHandler : IServerHandler<OrbDiscardedEvent>
{
    public OrbDiscardedEvent Handle(OrbDiscardedEvent networkEvent) => networkEvent;
}
