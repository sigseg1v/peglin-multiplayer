using System;
using Battle;
using BepInEx.Logging;
using Currency;
using HarmonyLib;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class PlayerStateApplier : IGameStateApplier<PlayerStateSnapshot>
{
    private readonly ManualLogSource _log;

    public PlayerStateApplier(ManualLogSource log) => _log = log;

    public void Apply(PlayerStateSnapshot snapshot)
    {
        try
        {
            ApplyHealth(snapshot);
            ApplyGold(snapshot);
            LogStatusEffects(snapshot);
        }
        catch (Exception ex)
        {
            _log.LogError($"[PlayerApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ApplyHealth(PlayerStateSnapshot snapshot)
    {
        var ctrl = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        if (ctrl == null)
        {
            _log.LogWarning("[PlayerApplier] PlayerHealthController not found in scene.");
            return;
        }

        // Access private FloatVariable fields via Harmony AccessTools
        var healthField = AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
        var maxHealthField = AccessTools.Field(typeof(PlayerHealthController), "_maxPlayerHealth");

        if (healthField == null || maxHealthField == null)
        {
            _log.LogWarning("[PlayerApplier] Could not find _playerHealth or _maxPlayerHealth fields.");
            return;
        }

        var healthVar = healthField.GetValue(ctrl) as FloatVariable;
        var maxHealthVar = maxHealthField.GetValue(ctrl) as FloatVariable;

        if (maxHealthVar != null)
            maxHealthVar.Set(snapshot.MaxHealth);

        if (healthVar != null)
            healthVar.Set(snapshot.CurrentHealth);

        _log.LogInfo($"[PlayerApplier] Health set to {snapshot.CurrentHealth}/{snapshot.MaxHealth}");
    }

    private void ApplyGold(PlayerStateSnapshot snapshot)
    {
        var currencyManager = CurrencyManager.Instance;
        if (currencyManager == null)
        {
            _log.LogWarning("[PlayerApplier] CurrencyManager.Instance is null.");
            return;
        }

        int currentGold = currencyManager.GoldAmount;
        int diff = snapshot.Gold - currentGold;

        if (diff > 0)
            currencyManager.AddGold(diff, silent: true);
        else if (diff < 0)
            currencyManager.RemoveGold(-diff, silent: true);

        _log.LogInfo($"[PlayerApplier] Gold set to {snapshot.Gold} (was {currentGold}, diff={diff})");
    }

    private void LogStatusEffects(PlayerStateSnapshot snapshot)
    {
        if (snapshot.StatusEffects == null || snapshot.StatusEffects.Count == 0)
            return;

        _log.LogInfo($"[PlayerApplier] Status effects ({snapshot.StatusEffects.Count}):");
        foreach (var effect in snapshot.StatusEffects)
        {
            _log.LogInfo($"  - {effect.EffectName} (type={effect.EffectType}, intensity={effect.Intensity})");
        }
    }
}
