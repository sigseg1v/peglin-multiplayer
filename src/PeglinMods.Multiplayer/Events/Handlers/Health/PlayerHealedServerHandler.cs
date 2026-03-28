namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class PlayerHealedServerHandler : IServerHandler<PlayerHealedEvent>
{
    public PlayerHealedEvent Handle(PlayerHealedEvent networkEvent) => networkEvent;
}
