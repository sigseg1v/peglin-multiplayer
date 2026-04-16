namespace Multipeglin.Events.Handlers.Health;

using Multipeglin.Events.Network.Health;

public sealed class PlayerDamagedServerHandler : IServerHandler<PlayerDamagedEvent>
{
    public PlayerDamagedEvent Handle(PlayerDamagedEvent networkEvent) => networkEvent;
}
