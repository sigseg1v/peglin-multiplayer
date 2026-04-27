using System;
using System.Linq;
using global::Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// On client: find this player's entry, write CurrentHealth/MaxHealth to the
/// local PlayerHealthController FloatVariables, and fire OnPlayerHealed so the
/// heal popup/animation plays. On host: no-op (host applied the heal directly).
/// </summary>
public sealed class PostBattleBossHealClientHandler : IClientHandler<PostBattleBossHealEvent>
{
    public void Handle(PostBattleBossHealEvent networkEvent)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null || networkEvent?.Entries == null)
            {
                return;
            }

            if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
            {
                return;
            }

            if (!services.TryResolve<PlayerRegistry>(out var playerRegistry))
            {
                return;
            }

            var mySlot = playerRegistry.LocalSlot;
            if (mySlot == null)
            {
                return;
            }

            var entry = networkEvent.Entries.FirstOrDefault(e => e.SlotIndex == mySlot.SlotIndex);
            if (entry == null || entry.HealedAmount <= 0)
            {
                return;
            }

            var phc = Resources.FindObjectsOfTypeAll<PlayerHealthController>().FirstOrDefault();
            if (phc != null)
            {
                SetFloatVariable(phc, "_playerHealth", entry.NewCurrentHealth);
                SetFloatVariable(phc, "_maxPlayerHealth", entry.MaxHealth);
            }

            PlayerHealthController.OnPlayerHealed?.Invoke(entry.HealedAmount);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[BossHeal] applied slot={entry.SlotIndex} +{entry.HealedAmount} -> {entry.NewCurrentHealth}/{entry.MaxHealth}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[BossHeal] handler failed: {ex.Message}");
        }
    }

    private static void SetFloatVariable(PlayerHealthController phc, string fieldName, float value)
    {
        var field = AccessTools.Field(typeof(PlayerHealthController), fieldName);
        var fv = field?.GetValue(phc);
        if (fv == null)
        {
            return;
        }

        var setter = AccessTools.Method(fv.GetType(), "Set", new[] { typeof(float) });
        setter?.Invoke(fv, new object[] { value });
    }
}
