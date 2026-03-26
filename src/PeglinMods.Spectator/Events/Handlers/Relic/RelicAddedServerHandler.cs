namespace PeglinMods.Spectator.Events.Handlers.Relic;

using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicAddedServerHandler : IServerHandler<RelicAddedEvent>
{
    public RelicAddedEvent Handle(RelicAddedEvent networkEvent) => networkEvent;
}
