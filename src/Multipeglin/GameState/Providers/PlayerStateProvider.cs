using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using UnityEngine;

namespace Multipeglin.GameState.Providers;

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
                // CurrentHealth is a public property
                snapshot.CurrentHealth = healthCtrl.CurrentHealth;

                // _maxPlayerHealth is a private FloatVariable field
                var maxHpVar = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth")?.GetValue(healthCtrl);
                if (maxHpVar != null)
                {
                    var valueProp = AccessTools.Property(maxHpVar.GetType(), "Value");
                    snapshot.MaxHealth = (float)(valueProp?.GetValue(maxHpVar) ?? 0f);
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
                if (SettingsManager.Instance != null)
                {
                    snapshot.SpeedupLevel = SettingsManager.Instance.SpeedupLevel;
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
