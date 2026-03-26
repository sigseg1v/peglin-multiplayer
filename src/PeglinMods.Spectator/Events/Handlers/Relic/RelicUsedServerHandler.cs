namespace PeglinMods.Spectator.Events.Handlers.Relic;

using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicUsedServerHandler : IServerHandler<RelicUsedEvent>
{
    public RelicUsedEvent Handle(RelicUsedEvent networkEvent) => networkEvent;
}
