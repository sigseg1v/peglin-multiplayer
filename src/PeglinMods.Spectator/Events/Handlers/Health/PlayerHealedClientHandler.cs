namespace PeglinMods.Spectator.Events.Handlers.Health;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class PlayerHealedClientHandler : IClientHandler<PlayerHealedEvent>
{
    public void Handle(PlayerHealedEvent networkEvent)
    {
        PlayerHealthController.OnPlayerHealed?.Invoke(networkEvent.Amount);
    }
}
