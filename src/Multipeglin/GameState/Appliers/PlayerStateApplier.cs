using System;
using System.Collections.Generic;
using Battle;
using BepInEx.Logging;
using Currency;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using UnityEngine;

namespace Multipeglin.GameState.Appliers;

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
            ApplySpeedup(snapshot);
            ApplyStatusEffects(snapshot);

            // === Post-apply verification ===
            VerifyPlayerState(snapshot);
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

        var healthField = AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
        var maxHealthField = AccessTools.Field(typeof(PlayerHealthController), "_maxPlayerHealth");

        if (healthField == null || maxHealthField == null)
        {
            _log.LogWarning("[PlayerApplier] Could not find _playerHealth or _maxPlayerHealth fields.");
            return;
        }

        var healthVar = healthField.GetValue(ctrl) as FloatVariable;
        var maxHealthVar = maxHealthField.GetValue(ctrl) as FloatVariable;

        float prevHealth = healthVar?.Value ?? 0;

        if (maxHealthVar != null)
            maxHealthVar.Set(snapshot.MaxHealth);

        if (healthVar != null)
            healthVar.Set(snapshot.CurrentHealth);

        // Force update the health bar UI if health changed
        if (System.Math.Abs(prevHealth - snapshot.CurrentHealth) > 0.1f)
        {
            try
            {
                // Call UpdateHealthBar to refresh the visual bar
                var updateMethod = AccessTools.Method(typeof(PlayerHealthController), "UpdateHealthBar");
                updateMethod?.Invoke(ctrl, null);
            }
            catch { }
        }

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

        Patches.MultiplayerClientPatches.AllowCurrencySync = true;
        try
        {
            if (diff > 0)
                currencyManager.AddGold(diff, silent: true);
            else if (diff < 0)
                currencyManager.RemoveGold(-diff, silent: true);
        }
        finally
        {
            Patches.MultiplayerClientPatches.AllowCurrencySync = false;
        }

        _log.LogInfo($"[PlayerApplier] Gold set to {snapshot.Gold} (was {currentGold}, diff={diff})");
    }

    private void ApplySpeedup(PlayerStateSnapshot snapshot)
    {
        try
        {
            var tsm = TimescaleManager.Instance;
            if (tsm == null) return;

            if (tsm.isSpedUp != snapshot.IsSpedUp)
            {
                tsm.isSpedUp = snapshot.IsSpedUp;
                Time.timeScale = snapshot.IsSpedUp ? snapshot.SpeedupLevel : 1f;
            }
        }
        catch { }
    }

    /// <summary>
    /// Apply the snapshot's status effects to the local PlayerStatusEffectController
    /// (clear + rebuild the internal list) and refresh the StatusEffectIconManager UI
    /// so the native buff icons above the player's head match the host's state.
    ///
    /// Runs on the client for its OWN slot — populated by
    /// GameStateApplyService.ApplyPlayerSnapshot from the CoopPlayerSummary.StatusEffects
    /// sent by the host each heartbeat. Enables Ballusion dodges, Intangiball caps,
    /// Ballwark absorption, etc. to all work correctly for non-host players.
    /// </summary>
    private void ApplyStatusEffects(PlayerStateSnapshot snapshot)
    {
        try
        {
            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl == null)
            {
                // Not in a battle scene — nothing to apply
                return;
            }

            var effectsField = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects");
            var effects = effectsField?.GetValue(statusCtrl) as List<Battle.StatusEffects.StatusEffect>;
            if (effects == null)
            {
                _log.LogWarning("[PlayerApplier] _statusEffects list is null; cannot apply status effects");
                return;
            }

            // Clear existing effects and rebuild from snapshot
            effects.Clear();

            if (snapshot.StatusEffects != null)
            {
                foreach (var entry in snapshot.StatusEffects)
                {
                    var effectType = (Battle.StatusEffects.StatusEffectType)entry.EffectType;
                    if (effectType == Battle.StatusEffects.StatusEffectType.None) continue;
                    if (entry.Intensity <= 0) continue;
                    effects.Add(new Battle.StatusEffects.StatusEffect(effectType, entry.Intensity));
                }
            }

            // Refresh the native status effect UI (icons above the player's head)
            var uiField = AccessTools.Field(typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffectUI");
            var ui = uiField?.GetValue(statusCtrl) as Battle.StatusEffects.StatusEffectIconManager;
            if (ui != null)
            {
                try
                {
                    ui.UpdateStatusEffects(effects.ToArray());
                }
                catch (Exception uiEx)
                {
                    _log.LogWarning($"[PlayerApplier] Status effect UI update failed: {uiEx.Message}");
                }
            }

            if (effects.Count > 0)
            {
                var names = string.Join(", ", snapshot.StatusEffects.ConvertAll(
                    e => $"{(Battle.StatusEffects.StatusEffectType)e.EffectType}={e.Intensity}"));
                _log.LogInfo($"[PlayerApplier] Status effects applied ({effects.Count}): [{names}]");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[PlayerApplier] ApplyStatusEffects failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Post-apply verification: re-read actual game state and compare with snapshot.
    /// Logs MISMATCH warnings for any differences, INFO on success.
    /// </summary>
    private void VerifyPlayerState(PlayerStateSnapshot snapshot)
    {
        try
        {
            bool allMatch = true;

            // Verify health
            var ctrl = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (ctrl != null)
            {
                var healthField = AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
                var maxHealthField = AccessTools.Field(typeof(PlayerHealthController), "_maxPlayerHealth");
                var healthVar = healthField?.GetValue(ctrl) as FloatVariable;
                var maxHealthVar = maxHealthField?.GetValue(ctrl) as FloatVariable;

                float actualHealth = healthVar?.Value ?? -1f;
                float actualMax = maxHealthVar?.Value ?? -1f;

                if (Math.Abs(actualHealth - snapshot.CurrentHealth) > 0.1f)
                {
                    _log.LogWarning($"[Verify] MISMATCH health: actual={actualHealth:F1} expected={snapshot.CurrentHealth:F1}");
                    allMatch = false;
                }
                if (Math.Abs(actualMax - snapshot.MaxHealth) > 0.1f)
                {
                    _log.LogWarning($"[Verify] MISMATCH maxHealth: actual={actualMax:F1} expected={snapshot.MaxHealth:F1}");
                    allMatch = false;
                }
            }

            // Verify gold
            var currencyManager = CurrencyManager.Instance;
            if (currencyManager != null)
            {
                int actualGold = currencyManager.GoldAmount;
                if (actualGold != snapshot.Gold)
                {
                    _log.LogWarning($"[Verify] MISMATCH gold: actual={actualGold} expected={snapshot.Gold}");
                    allMatch = false;
                }
            }

            if (allMatch)
                _log.LogInfo($"[Verify] PlayerState OK: health={snapshot.CurrentHealth:F0}/{snapshot.MaxHealth:F0} gold={snapshot.Gold}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[Verify] PlayerState verification failed: {ex.Message}");
        }
    }
}
