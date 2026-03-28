namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class PlayerDamagedServerHandler : IServerHandler<PlayerDamagedEvent>
{
    public PlayerDamagedEvent Handle(PlayerDamagedEvent networkEvent) => networkEvent;
}
