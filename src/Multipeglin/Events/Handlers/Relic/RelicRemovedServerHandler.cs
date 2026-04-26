
using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;
public sealed class RelicRemovedServerHandler : IServerHandler<RelicRemovedEvent>
{
    public RelicRemovedEvent Handle(RelicRemovedEvent networkEvent) => networkEvent;
}
