namespace PeglinMods.Spectator.Events.Handlers.Health;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class PlayerDamagedClientHandler : IClientHandler<PlayerDamagedEvent>
{
    public void Handle(PlayerDamagedEvent networkEvent)
    {
        PlayerHealthController.OnPlayerDamaged?.Invoke(networkEvent.Damage);
    }
}
