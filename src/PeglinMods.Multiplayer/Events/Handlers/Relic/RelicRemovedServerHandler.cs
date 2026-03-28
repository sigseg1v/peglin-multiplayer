namespace PeglinMods.Multiplayer.Events.Handlers.Relic;

using PeglinMods.Multiplayer.Events.Network.Relic;

public sealed class RelicRemovedServerHandler : IServerHandler<RelicRemovedEvent>
{
    public RelicRemovedEvent Handle(RelicRemovedEvent networkEvent) => networkEvent;
}
