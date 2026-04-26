using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;

public sealed class RelicUsedServerHandler : IServerHandler<RelicUsedEvent>
{
    public RelicUsedEvent Handle(RelicUsedEvent networkEvent) => networkEvent;
}
