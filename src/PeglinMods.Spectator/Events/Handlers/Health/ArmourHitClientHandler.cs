namespace PeglinMods.Spectator.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class ArmourHitClientHandler : IClientHandler<ArmourHitEvent>
{
    public void Handle(ArmourHitEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnArmourHit?.Invoke(networkEvent.Damage);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"ArmourHit handler failed: {e.Message}");
        }
    }
}
