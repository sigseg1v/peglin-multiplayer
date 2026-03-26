namespace PeglinMods.Spectator.Events.Handlers.Relic;

using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicRemovedServerHandler : IServerHandler<RelicRemovedEvent>
{
    public RelicRemovedEvent Handle(RelicRemovedEvent networkEvent) => networkEvent;
}
