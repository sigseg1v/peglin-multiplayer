
using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;
public sealed class RelicAddedServerHandler : IServerHandler<RelicAddedEvent>
{
    public RelicAddedEvent Handle(RelicAddedEvent networkEvent) => networkEvent;
}
