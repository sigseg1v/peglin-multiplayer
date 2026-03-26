namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class OrbDiscardedServerHandler : IServerHandler<OrbDiscardedEvent>
{
    public OrbDiscardedEvent Handle(OrbDiscardedEvent networkEvent) => networkEvent;
}
