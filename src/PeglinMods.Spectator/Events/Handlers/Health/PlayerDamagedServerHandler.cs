namespace PeglinMods.Spectator.Events.Handlers.Health;

using PeglinMods.Spectator.Events.Network.Health;

public sealed class PlayerDamagedServerHandler : IServerHandler<PlayerDamagedEvent>
{
    public PlayerDamagedEvent Handle(PlayerDamagedEvent networkEvent) => networkEvent;
}
