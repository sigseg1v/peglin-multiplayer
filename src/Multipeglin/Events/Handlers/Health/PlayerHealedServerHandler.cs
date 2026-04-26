using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class PlayerHealedServerHandler : IServerHandler<PlayerHealedEvent>
{
    public PlayerHealedEvent Handle(PlayerHealedEvent networkEvent) => networkEvent;
}
