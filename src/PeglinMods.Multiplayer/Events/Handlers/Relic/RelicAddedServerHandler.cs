namespace PeglinMods.Multiplayer.Events.Handlers.Relic;

using PeglinMods.Multiplayer.Events.Network.Relic;

public sealed class RelicAddedServerHandler : IServerHandler<RelicAddedEvent>
{
    public RelicAddedEvent Handle(RelicAddedEvent networkEvent) => networkEvent;
}
