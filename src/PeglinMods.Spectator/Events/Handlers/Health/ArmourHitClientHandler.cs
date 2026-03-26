namespace PeglinMods.Spectator.Events.Handlers.Health;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class ArmourHitClientHandler : IClientHandler<ArmourHitEvent>
{
    public void Handle(ArmourHitEvent networkEvent)
    {
        PlayerHealthController.OnArmourHit?.Invoke(networkEvent.Damage);
    }
}
