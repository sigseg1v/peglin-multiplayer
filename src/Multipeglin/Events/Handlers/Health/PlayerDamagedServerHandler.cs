using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class PlayerDamagedServerHandler : IServerHandler<PlayerDamagedEvent>
{
    public PlayerDamagedEvent Handle(PlayerDamagedEvent networkEvent) => networkEvent;
}
