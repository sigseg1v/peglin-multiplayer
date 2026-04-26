using System;
using global::Battle;
using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

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
            MultiplayerPlugin.Logger.LogWarning($"ArmourHit handler failed: {e.Message}");
        }
    }
}
