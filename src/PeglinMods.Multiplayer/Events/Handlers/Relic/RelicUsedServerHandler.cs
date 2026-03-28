namespace PeglinMods.Multiplayer.Events.Handlers.Relic;

using PeglinMods.Multiplayer.Events.Network.Relic;

public sealed class RelicUsedServerHandler : IServerHandler<RelicUsedEvent>
{
    public RelicUsedEvent Handle(RelicUsedEvent networkEvent) => networkEvent;
}
