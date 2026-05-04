using System;
using global::Battle;
using HarmonyLib;
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

            // Only apply the heal when it targeted *this* player. Otherwise every client
            // (and host) would react to heals that weren't for them.
            if (networkEvent.TargetSlotIndex >= 0)
            {
                var mySlot = CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
                if (mySlot >= 0 && networkEvent.TargetSlotIndex != mySlot)
                {
                    return;
                }
            }

            // Set health directly from host data — the dumb client never derives its
            // own state, so we must write the authoritative value into the FloatVariable.
            var ctrl = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (ctrl != null)
            {
                var healthField = AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
                var healthVar = healthField?.GetValue(ctrl) as FloatVariable;
                healthVar?.Set(networkEvent.RemainingHealth);
            }

            PlayerHealthController.OnPlayerHealed?.Invoke(networkEvent.Amount);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PlayerHealed handler failed: {e.Message}");
        }
    }
}
