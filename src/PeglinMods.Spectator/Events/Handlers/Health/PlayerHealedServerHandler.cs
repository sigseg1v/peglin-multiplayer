namespace PeglinMods.Spectator.Events.Handlers.Health;

using PeglinMods.Spectator.Events.Network.Health;

public sealed class PlayerHealedServerHandler : IServerHandler<PlayerHealedEvent>
{
    public PlayerHealedEvent Handle(PlayerHealedEvent networkEvent) => networkEvent;
}
