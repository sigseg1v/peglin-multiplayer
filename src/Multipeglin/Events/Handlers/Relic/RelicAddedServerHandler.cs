namespace Multipeglin.Events.Handlers.Relic;

using Multipeglin.Events.Network.Relic;

public sealed class RelicAddedServerHandler : IServerHandler<RelicAddedEvent>
{
    public RelicAddedEvent Handle(RelicAddedEvent networkEvent) => networkEvent;
}
