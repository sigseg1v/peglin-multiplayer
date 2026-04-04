using System;
using Battle;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

/// <summary>
/// Subscribes to battle events to distribute enemy damage to all co-op players.
///
/// During the enemy attack phase, the game's PlayerHealthController applies damage
/// to whichever player is currently loaded in the singletons (the active player).
/// This class captures the damage delta and applies it to all OTHER players' stored
/// CoopPlayerState so everyone takes the same hit.
/// </summary>
public sealed class CoopSubscriptions
{
    private readonly IMultiplayerMode _mode;
    private readonly CoopStateManager _coopStateManager;
    private readonly ManualLogSource _log;

    /// <summary>
    /// Track health before the enemy attack phase to compute the damage delta.
    /// </summary>
    private float _healthBeforeAttack;

    public CoopSubscriptions(IMultiplayerMode mode, CoopStateManager coopStateManager, ManualLogSource log)
    {
        _mode = mode;
        _coopStateManager = coopStateManager;
        _log = log;
    }

    public void Subscribe()
    {
        BattleController.OnAttackStarted += OnAttackStarted;
        BattleController.OnTurnComplete += OnTurnComplete;
        _log.LogInfo("CoopSubscriptions registered");
    }

    public void Unsubscribe()
    {
        BattleController.OnAttackStarted -= OnAttackStarted;
        BattleController.OnTurnComplete -= OnTurnComplete;
    }

    /// <summary>
    /// Called when the enemy attack phase begins. Snapshot the active player's
    /// current health so we can compute how much damage was dealt after it resolves.
    /// </summary>
    private void OnAttackStarted()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        try
        {
            var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (phc == null)
            {
                _healthBeforeAttack = 0f;
                return;
            }

            _healthBeforeAttack = phc.CurrentHealth;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] OnAttackStarted failed: {ex.Message}");
            _healthBeforeAttack = 0f;
        }
    }

    /// <summary>
    /// Called after the enemy attack phase completes. Compute the damage dealt
    /// to the active player and apply it to all other players' stored state.
    /// Then check if any player is dead.
    /// </summary>
    private void OnTurnComplete()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        try
        {
            var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (phc == null) return;

            float currentHealth = phc.CurrentHealth;
            float damage = _healthBeforeAttack - currentHealth;

            if (damage <= 0f) return;

            // Save the active player's state so it reflects the damage already taken
            _coopStateManager.SaveActivePlayerState();

            // Apply the same damage to all non-active players
            int activeSlot = _coopStateManager.ActivePlayerSlot;
            foreach (var state in _coopStateManager.PlayerStates.Values)
            {
                if (state.SlotIndex == activeSlot) continue;

                float oldHp = state.CurrentHealth;
                state.CurrentHealth = Mathf.Max(0f, state.CurrentHealth - damage);
                _log.LogInfo($"[CoopSubs] Slot {state.SlotIndex} ({state.PlayerName}): " +
                    $"hp {oldHp} -> {state.CurrentHealth} (damage={damage})");
            }

            // Check if any player died
            if (_coopStateManager.AnyPlayerDead)
            {
                _log.LogWarning("[CoopSubs] A co-op player has died! Triggering defeat.");
                // The game's defeat flow is driven by PlayerHealthController.OnDefeat
                // which fires when the active player's health reaches 0.
                // For non-active players dying, we need to trigger it manually.
                // Check if the active player is still alive -- if so, the game
                // won't trigger defeat on its own.
                if (currentHealth > 0f)
                {
                    _log.LogInfo("[CoopSubs] Active player alive but another player died. " +
                        "Setting active player health to 0 to trigger game over.");
                    phc.Damage(currentHealth, false);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] OnTurnComplete damage distribution failed: {ex.Message}");
        }
    }
}
