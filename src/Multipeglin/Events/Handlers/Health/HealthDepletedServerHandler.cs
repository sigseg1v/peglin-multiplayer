using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class HealthDepletedServerHandler : IServerHandler<HealthDepletedEvent>
{
    public HealthDepletedEvent Handle(HealthDepletedEvent networkEvent) => networkEvent;
}
