using System;
using global::Battle;
using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class MaxHealthChangedClientHandler : IClientHandler<MaxHealthChangedEvent>
{
    public void Handle(MaxHealthChangedEvent networkEvent)
    {
        try
        {
            // During native post-battle rewards, the client's health is managed locally.
            if (Coop.CoopRewardState.ClientInNativeRewardPhase)
            {
                return;
            }

            PlayerHealthController.OnPlayerMaxHealthChanged?.Invoke(networkEvent.NewMaxHealth);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"MaxHealthChanged handler failed: {e.Message}");
        }
    }
}
