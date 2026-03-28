namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class OrbDiscardedServerHandler : IServerHandler<OrbDiscardedEvent>
{
    public OrbDiscardedEvent Handle(OrbDiscardedEvent networkEvent) => networkEvent;
}
