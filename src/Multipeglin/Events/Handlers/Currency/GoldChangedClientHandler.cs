using System;
using HarmonyLib;
using Multipeglin.Events.Network.Currency;

namespace Multipeglin.Events.Handlers.Currency;

public sealed class GoldChangedClientHandler : IClientHandler<GoldChangedEvent>
{
    public void Handle(GoldChangedEvent networkEvent)
    {
        try
        {
            // During the native post-battle reward phase, the client's gold is being
            // modified by BattleUpgradeCanvas (orb purchases, upgrades, heals). Don't
            // overwrite with the host's gold changes — each player has their own gold.
            if (Coop.CoopRewardState.ClientInNativeRewardPhase)
            {
                MultiplayerPlugin.Logger.LogInfo(
                    $"Multiplayer: Ignoring GoldChanged during reward phase (host gold {networkEvent.NewAmount}, keeping client's local gold)");
                return;
            }

            // In coop, each player has their own gold and it is synced per-slot via
            // the periodic PlayerState heartbeat. Host-side gold changes (e.g. the
            // host purchasing items in their own shop) must NOT flow to the client's
            // CurrencyManager — they'd desync the client's visible gold until the
            // next heartbeat corrects it. The host already suppresses this dispatch
            // in coop; this is defense in depth.
            var services = global::Multipeglin.MultiplayerPlugin.Services;
            if (services != null
                && services.TryResolve<global::Multipeglin.GameState.CoopStateManager>(out var coop)
                && coop.TotalPlayerCount > 1)
            {
                MultiplayerPlugin.Logger.LogInfo(
                    $"Multiplayer: Ignoring GoldChanged in coop (host gold {networkEvent.NewAmount}, client gold is per-slot)");
                return;
            }

            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Gold changed {networkEvent.PreviousAmount} -> {networkEvent.NewAmount} ({(networkEvent.IsGain ? "+" : string.Empty)}{networkEvent.Delta})");

            var cm = UnityEngine.Object.FindObjectOfType<global::Currency.CurrencyManager>();
            if (cm != null)
            {
                // CurrencyManager.GoldAmount is a public auto-property with protected set.
                // Use AccessTools to find the backing field.
                var field = AccessTools.Field(typeof(global::Currency.CurrencyManager), "<GoldAmount>k__BackingField");
                field?.SetValue(cm, networkEvent.NewAmount);
            }

            if (networkEvent.IsGain)
            {
                global::Currency.CurrencyManager.OnGoldAdded?.Invoke(networkEvent.PreviousAmount, networkEvent.Delta, false);
            }
            else
            {
                global::Currency.CurrencyManager.OnGoldRemoved?.Invoke(networkEvent.PreviousAmount, -networkEvent.Delta, false);
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Gold update failed: {e.Message}");
        }
    }
}
