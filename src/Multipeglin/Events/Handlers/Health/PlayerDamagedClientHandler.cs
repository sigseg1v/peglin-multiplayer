namespace Multipeglin.Events.Handlers.Health;

using System;
using global::Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Health;
using Multipeglin.Multiplayer;
using UnityEngine;

public sealed class PlayerDamagedClientHandler : IClientHandler<PlayerDamagedEvent>
{
    public void Handle(PlayerDamagedEvent e)
    {
        try
        {
            // During native post-battle rewards, the client's health is managed locally.
            if (Coop.CoopRewardState.ClientInNativeRewardPhase) return;

            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Set health directly from host data
            var ctrl = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (ctrl != null)
            {
                var healthField = AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
                var maxHealthField = AccessTools.Field(typeof(PlayerHealthController), "_maxPlayerHealth");
                var healthVar = healthField?.GetValue(ctrl) as FloatVariable;
                var maxHealthVar = maxHealthField?.GetValue(ctrl) as FloatVariable;

                if (maxHealthVar != null) maxHealthVar.Set(e.MaxHealth);
                if (healthVar != null) healthVar.Set(e.RemainingHealth);
            }

            // Fire the event for UI animations (health bar shake, flash, etc.)
            PlayerHealthController.OnPlayerDamaged?.Invoke(e.Damage);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"PlayerDamaged handler failed: {ex.Message}");
        }
    }
}
