namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class HealthDepletedServerHandler : IServerHandler<HealthDepletedEvent>
{
    public HealthDepletedEvent Handle(HealthDepletedEvent networkEvent) => networkEvent;
}
