using System;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using UnityEngine;

namespace Multipeglin.GameState.Providers;

public class PlayerStateProvider : IGameStateProvider<PlayerStateSnapshot>
{
    private readonly ManualLogSource _log;

    private static readonly System.Reflection.FieldInfo _maxPlayerHealthField
        = AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");

    private static readonly System.Reflection.PropertyInfo _currencyInstanceProp
        = AccessTools.Property(typeof(Currency.CurrencyManager), "Instance");

    private static readonly System.Reflection.PropertyInfo _currencyGoldProp
        = AccessTools.Property(typeof(Currency.CurrencyManager), "GoldAmount");

    private static readonly System.Reflection.FieldInfo _playerStatusEffectsField
        = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects");

    private static readonly System.Reflection.PropertyInfo _timescaleSpedUpProp
        = AccessTools.Property(typeof(TimescaleManager), "isSpedUp");

    // FloatVariable.Value getter — resolved on first use because we don't have
    // a static reference to the concrete FloatVariable type at compile time.
    private static System.Reflection.PropertyInfo _floatVariableValueProp;

    private static readonly System.Collections.Generic.Dictionary<Type, (System.Reflection.FieldInfo type, System.Reflection.FieldInfo intensity)>
        _statusEffectFieldCache = new System.Collections.Generic.Dictionary<Type, (System.Reflection.FieldInfo, System.Reflection.FieldInfo)>(8);

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
                // Clamp negative HP to 0. Fatal damage sets the FloatVariable below 0,
                // and if this capture runs before CheckForDeathAndUpdateBar_Prefix normalizes
                // it, the client would render -5/100. Never send negative HP over the wire.
                snapshot.CurrentHealth = Mathf.Max(0f, healthCtrl.CurrentHealth);

                // _maxPlayerHealth is a private FloatVariable field
                var maxHpVar = _maxPlayerHealthField?.GetValue(healthCtrl);
                if (maxHpVar != null)
                {
                    _floatVariableValueProp ??= AccessTools.Property(maxHpVar.GetType(), "Value");
                    snapshot.MaxHealth = (float)(_floatVariableValueProp?.GetValue(maxHpVar) ?? 0f);
                }
            }

            // Gold - via CurrencyManager singleton
            try
            {
                var cm = _currencyInstanceProp?.GetValue(null);
                if (cm != null)
                {
                    snapshot.Gold = (int)(_currencyGoldProp?.GetValue(cm) ?? 0);
                }
            }
            catch
            {
            }

            // Status effects
            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl != null)
            {
                try
                {
                    var effectsList = _playerStatusEffectsField?.GetValue(statusCtrl) as System.Collections.IList;
                    if (effectsList != null)
                    {
                        foreach (var effect in effectsList)
                        {
                            var fields = GetStatusEffectFields(effect.GetType());
                            if (fields.type != null)
                            {
                                var effectType = fields.type.GetValue(effect);
                                snapshot.StatusEffects.Add(new StatusEffectEntry
                                {
                                    EffectType = (int)effectType,
                                    EffectName = effectType.ToString(),
                                    Intensity = (int)(fields.intensity?.GetValue(effect) ?? 0),
                                });
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            // Speed state
            try
            {
                var tsInstance = UnityEngine.Object.FindObjectOfType<TimescaleManager>();
                if (tsInstance != null)
                {
                    snapshot.IsSpedUp = (bool)(_timescaleSpedUpProp?.GetValue(tsInstance) ?? false);
                }

                if (SettingsManager.Instance != null)
                {
                    snapshot.SpeedupLevel = SettingsManager.Instance.SpeedupLevel;
                }
            }
            catch
            {
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PlayerStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private static (System.Reflection.FieldInfo type, System.Reflection.FieldInfo intensity) GetStatusEffectFields(Type t)
    {
        if (_statusEffectFieldCache.TryGetValue(t, out var cached))
        {
            return cached;
        }

        cached = (AccessTools.Field(t, "EffectType"), AccessTools.Field(t, "Intensity"));
        _statusEffectFieldCache[t] = cached;
        return cached;
    }
}
