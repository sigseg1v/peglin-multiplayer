namespace PeglinMods.Spectator.Events.Handlers.Health;

using PeglinMods.Spectator.Events.Network.Health;

public sealed class HealthDepletedServerHandler : IServerHandler<HealthDepletedEvent>
{
    public HealthDepletedEvent Handle(HealthDepletedEvent networkEvent) => networkEvent;
}
