using System;
using global::Battle;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class PlayerHealedClientHandler : IClientHandler<PlayerHealedEvent>
{
    public void Handle(PlayerHealedEvent networkEvent)
    {
        try
        {
            // During native post-battle rewards, the client's health is managed locally.
            if (Coop.CoopRewardState.ClientInNativeRewardPhase)
            {
                return;
            }

            // Only fire the heal delegate when the heal targeted *this* player. Otherwise
            // every client (and host) sees subscribers react to heals that weren't for them.
            if (networkEvent.TargetSlotIndex >= 0)
            {
                var mySlot = CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
                if (mySlot >= 0 && networkEvent.TargetSlotIndex != mySlot)
                {
                    return;
                }
            }

            PlayerHealthController.OnPlayerHealed?.Invoke(networkEvent.Amount);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PlayerHealed handler failed: {e.Message}");
        }
    }
}
