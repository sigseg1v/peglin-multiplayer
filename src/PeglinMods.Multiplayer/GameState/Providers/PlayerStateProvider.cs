using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class PlayerStateProvider : IGameStateProvider<PlayerStateSnapshot>
{
    private readonly ManualLogSource _log;

    public PlayerStateProvider(ManualLogSource log) => _log = log;

    public PlayerStateSnapshot Capture()
    {
        try
        {
            var snapshot = new PlayerStateSnapshot();

            // Health - via PlayerHealthController
            var healthCtrl = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
            if (healthCtrl != null)
            {
                var healthField = AccessTools.Field(typeof(Battle.PlayerHealthController), "playerHealth");
                var maxHealthField = AccessTools.Field(typeof(Battle.PlayerHealthController), "maxPlayerHealth");
                var healthVar = healthField?.GetValue(healthCtrl);
                var maxHealthVar = maxHealthField?.GetValue(healthCtrl);

                if (healthVar != null)
                {
                    var valueProp = AccessTools.Property(healthVar.GetType(), "Value");
                    snapshot.CurrentHealth = (float)(valueProp?.GetValue(healthVar) ?? 0f);
                }
                if (maxHealthVar != null)
                {
                    var valueProp = AccessTools.Property(maxHealthVar.GetType(), "Value");
                    snapshot.MaxHealth = (float)(valueProp?.GetValue(maxHealthVar) ?? 0f);
                }
            }

            // Gold - via CurrencyManager singleton
            try
            {
                var cmType = typeof(Currency.CurrencyManager);
                var instanceProp = AccessTools.Property(cmType, "Instance");
                var cm = instanceProp?.GetValue(null);
                if (cm != null)
                {
                    var goldProp = AccessTools.Property(cmType, "GoldAmount");
                    snapshot.Gold = (int)(goldProp?.GetValue(cm) ?? 0);
                }
            }
            catch { }

            // Status effects
            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl != null)
            {
                try
                {
                    var effectsList = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects")
                        ?.GetValue(statusCtrl) as System.Collections.IList;
                    if (effectsList != null)
                    {
                        foreach (var effect in effectsList)
                        {
                            var typeField = AccessTools.Field(effect.GetType(), "EffectType");
                            var intensityField = AccessTools.Field(effect.GetType(), "Intensity");
                            if (typeField != null)
                            {
                                var effectType = typeField.GetValue(effect);
                                snapshot.StatusEffects.Add(new StatusEffectEntry
                                {
                                    EffectType = (int)effectType,
                                    EffectName = effectType.ToString(),
                                    Intensity = (int)(intensityField?.GetValue(effect) ?? 0)
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            // Speed state
            try
            {
                var tsInstance = UnityEngine.Object.FindObjectOfType<TimescaleManager>();
                if (tsInstance != null)
                {
                    var spedUpProp = AccessTools.Property(typeof(TimescaleManager), "isSpedUp");
                    snapshot.IsSpedUp = (bool)(spedUpProp?.GetValue(tsInstance) ?? false);
                }
            }
            catch { }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PlayerStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
