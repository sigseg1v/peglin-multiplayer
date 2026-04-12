namespace PeglinMods.Multiplayer.Events.Handlers.Currency;

using System;
using HarmonyLib;
using PeglinMods.Multiplayer.Events.Network.Currency;
using UnityEngine;

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

            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Gold changed {networkEvent.PreviousAmount} -> {networkEvent.NewAmount} ({(networkEvent.IsGain ? "+" : "")}{networkEvent.Delta})");

            var cm = UnityEngine.Object.FindObjectOfType<global::Currency.CurrencyManager>();
            if (cm != null)
            {
                // CurrencyManager.GoldAmount is a public auto-property with protected set.
                // Use AccessTools to find the backing field.
                var field = AccessTools.Field(typeof(global::Currency.CurrencyManager), "<GoldAmount>k__BackingField");
                if (field != null)
                    field.SetValue(cm, networkEvent.NewAmount);
            }

            if (networkEvent.IsGain)
                global::Currency.CurrencyManager.OnGoldAdded?.Invoke(networkEvent.PreviousAmount, networkEvent.Delta, false);
            else
                global::Currency.CurrencyManager.OnGoldRemoved?.Invoke(networkEvent.PreviousAmount, -networkEvent.Delta, false);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Gold update failed: {e.Message}");
        }
    }
}
