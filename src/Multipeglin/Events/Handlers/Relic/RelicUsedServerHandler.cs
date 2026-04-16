namespace Multipeglin.Events.Handlers.Relic;

using Multipeglin.Events.Network.Relic;

public sealed class RelicUsedServerHandler : IServerHandler<RelicUsedEvent>
{
    public RelicUsedEvent Handle(RelicUsedEvent networkEvent) => networkEvent;
}
