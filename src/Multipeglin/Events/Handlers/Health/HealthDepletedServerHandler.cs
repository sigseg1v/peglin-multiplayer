namespace Multipeglin.Events.Handlers.Health;

using Multipeglin.Events.Network.Health;

public sealed class HealthDepletedServerHandler : IServerHandler<HealthDepletedEvent>
{
    public HealthDepletedEvent Handle(HealthDepletedEvent networkEvent) => networkEvent;
}
