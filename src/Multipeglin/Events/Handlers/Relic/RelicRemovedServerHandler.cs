namespace Multipeglin.Events.Handlers.Relic;

using Multipeglin.Events.Network.Relic;

public sealed class RelicRemovedServerHandler : IServerHandler<RelicRemovedEvent>
{
    public RelicRemovedEvent Handle(RelicRemovedEvent networkEvent) => networkEvent;
}
