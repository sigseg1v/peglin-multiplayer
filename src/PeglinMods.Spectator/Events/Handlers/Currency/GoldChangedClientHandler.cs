namespace PeglinMods.Spectator.Events.Handlers.Currency;

using System;
using HarmonyLib;
using PeglinMods.Spectator.Events.Network.Currency;
using UnityEngine;

public sealed class GoldChangedClientHandler : IClientHandler<GoldChangedEvent>
{
    public void Handle(GoldChangedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Gold changed {networkEvent.PreviousAmount} -> {networkEvent.NewAmount} ({(networkEvent.IsGain ? "+" : "")}{networkEvent.Delta})");

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
            SpectatorPlugin.Logger.LogWarning($"Gold update failed: {e.Message}");
        }
    }
}
