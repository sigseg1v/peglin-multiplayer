namespace Multipeglin.Events.Handlers.Health;

using Multipeglin.Events.Network.Health;

public sealed class PlayerHealedServerHandler : IServerHandler<PlayerHealedEvent>
{
    public PlayerHealedEvent Handle(PlayerHealedEvent networkEvent) => networkEvent;
}
