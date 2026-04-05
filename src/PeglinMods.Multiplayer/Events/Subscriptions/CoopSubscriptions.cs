using System;
using Battle;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

/// <summary>
/// Subscribes to battle events to distribute enemy damage to all co-op players
/// and to drive the turn system (TurnManager) during co-op battles.
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
    private readonly TurnManager _turnManager;
    private readonly ManualLogSource _log;

    /// <summary>
    /// Track health before the enemy attack phase to compute the damage delta.
    /// </summary>
    private float _healthBeforeAttack;

    /// <summary>
    /// Singleton instance so the BattleController.Awake Postfix can re-subscribe.
    /// The game's static delegates may lose subscribers across scene loads,
    /// so we re-subscribe at the start of every battle.
    /// </summary>
    internal static CoopSubscriptions Instance { get; private set; }

    public CoopSubscriptions(IMultiplayerMode mode, CoopStateManager coopStateManager, TurnManager turnManager, ManualLogSource log)
    {
        _mode = mode;
        _coopStateManager = coopStateManager;
        _turnManager = turnManager;
        _log = log;
        Instance = this;
    }

    public void Subscribe()
    {
        // Unsubscribe first to avoid duplicate handlers if called multiple times
        Unsubscribe();

        BattleController.OnAttackStarted += OnAttackStarted;
        BattleController.OnTurnComplete += OnTurnComplete;
        BattleController.OnBattleStarted += OnBattleStarted;
        BattleController.OnStartedAwaitingShot += OnAwaitingShot;
        BattleController.OnShotComplete += OnShotComplete;
        BattleController.OnVictory += OnVictory;
        _log.LogInfo($"CoopSubscriptions registered (with turn system) — TotalPlayerCount={_coopStateManager.TotalPlayerCount}, IsHosting={_mode.IsHosting}");
    }

    public void Unsubscribe()
    {
        BattleController.OnAttackStarted -= OnAttackStarted;
        BattleController.OnTurnComplete -= OnTurnComplete;
        BattleController.OnBattleStarted -= OnBattleStarted;
        BattleController.OnStartedAwaitingShot -= OnAwaitingShot;
        BattleController.OnShotComplete -= OnShotComplete;
        BattleController.OnVictory -= OnVictory;
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

    // =========================================================================
    // TURN SYSTEM — drive TurnManager from battle lifecycle events
    // =========================================================================

    /// <summary>
    /// Called when a battle starts. Build the turn order and start round 1.
    /// </summary>
    private void OnBattleStarted()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        _turnManager.BuildTurnOrder();
        _turnManager.StartNewRound();
        BroadcastTurnChange();

        _log.LogInfo($"[CoopSubs] Battle started — turn order built, round 1 started, slot {_turnManager.CurrentPlayerSlot} is up");
    }

    /// <summary>
    /// Called when the game is ready for a shot (awaiting player input).
    /// If all players have shot, move to damage phase. Otherwise broadcast
    /// whose turn it is.
    /// </summary>
    private void OnAwaitingShot()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        if (_turnManager.Phase == TurnPhase.ALL_DONE)
        {
            _turnManager.EnterDamagePhase();
            BroadcastTurnChange();
        }
        else if (_turnManager.Phase != TurnPhase.PLAYER_AIMING)
        {
            // New round: swap to first player (host, slot 0)
            _turnManager.StartNewRound();
            if (_turnManager.CurrentPlayerSlot >= 0)
            {
                _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);
                EnsureBattleDeckPopulated("new round");
            }
            BroadcastTurnChange();
        }
    }

    /// <summary>
    /// Called when a shot resolves. Advance to the next player's turn.
    /// If not all players have shot, redirect BattleController back to
    /// AWAITING_SHOT so the next player can aim. The game has already set
    /// _battleState to PRE_ATTACK_SPAWN_CHECK before invoking OnShotComplete,
    /// so we override it here to prevent the attack/damage phase from starting.
    /// </summary>
    private void OnShotComplete()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        // Mark current player's shot as fired before advancing
        _turnManager.MarkShotFired();

        // Save current player's state before swapping
        _coopStateManager.SaveActivePlayerState();

        _turnManager.AdvanceTurn();

        // If advancing to a new player, swap their state into the singletons
        // and redirect the BattleController back to AWAITING_SHOT so the game
        // draws the next orb and waits for the next player's shot.
        if (_turnManager.Phase == TurnPhase.PLAYER_AIMING && _turnManager.CurrentPlayerSlot >= 0)
        {
            _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);

            // After swapping deck state, ensure the battle deck is usable.
            EnsureBattleDeckPopulated("shot complete swap");

            // CRITICAL: The BattleController already set _battleState to
            // PRE_ATTACK_SPAWN_CHECK (or DO_ATTACK) before firing OnShotComplete.
            // Override it back to AWAITING_SHOT so the state machine re-enters
            // the aiming phase instead of proceeding to the attack/damage phase.
            BattleController.CurrentBattleState = BattleController.BattleState.AWAITING_SHOT;

            // Manually trigger DrawBall since we bypassed the normal
            // AWAITING_ENEMY_CLEANUP → ChooseShuffleOrDrawAtEndOfTurn flow
            // that would normally create the PachinkoBall.
            try
            {
                var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
                if (bc != null)
                {
                    var drawBallMethod = AccessTools.Method(typeof(BattleController), "DrawBall");
                    if (drawBallMethod != null)
                    {
                        drawBallMethod.Invoke(bc, null);
                        _log.LogInfo($"[CoopSubs] Manually called DrawBall for slot {_turnManager.CurrentPlayerSlot}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                _log.LogWarning($"[CoopSubs] DrawBall call failed: {ex.Message}");
            }

            _log.LogInfo($"[CoopSubs] Swapped to player slot {_turnManager.CurrentPlayerSlot}, " +
                $"redirected BattleState -> AWAITING_SHOT");
        }

        BroadcastTurnChange();

        _log.LogInfo($"[CoopSubs] Shot complete — advanced turn. Phase={_turnManager.Phase}, slot={_turnManager.CurrentPlayerSlot}");
    }

    // =========================================================================
    // POST-BATTLE REWARDS — send reward choices to non-host players
    // =========================================================================

    /// <summary>
    /// Called when the battle is won. Save active player state and send reward
    /// choices to each non-host player so they can independently pick rewards.
    /// The host picks rewards via the normal game UI.
    /// </summary>
    private void OnVictory()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        try
        {
            // Save active player state before reward selection
            _coopStateManager.SaveActivePlayerState();

            // Generate reward choices for each non-host player
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var registry) != true) return;

            foreach (var kvp in _coopStateManager.PlayerStates)
            {
                if (kvp.Key == 0) continue; // Host picks rewards via the normal game UI

                var options = new System.Collections.Generic.List<RewardOption>();
                int idx = 0;

                // Option 1: Heal 20 HP
                options.Add(new RewardOption
                {
                    OptionIndex = idx++,
                    Type = "heal",
                    DisplayName = "Heal 20 HP",
                    Description = "Restore 20 hit points",
                });

                // Option 2: +5 max HP
                options.Add(new RewardOption
                {
                    OptionIndex = idx++,
                    Type = "max_hp",
                    DisplayName = "+5 Max HP",
                    Description = "Permanently gain 5 max hit points",
                });

                // Option 3: Skip (small gold reward)
                options.Add(new RewardOption
                {
                    OptionIndex = idx++,
                    Type = "skip",
                    DisplayName = "Skip",
                    Description = "Skip this reward and gain 10 gold",
                    GoldReward = 10,
                });

                registry.Dispatch(new RewardChoicesEvent
                {
                    TargetSlotIndex = kvp.Key,
                    Options = options,
                });

                _log.LogInfo($"[CoopSubs] Sent {options.Count} reward choices to slot {kvp.Key}");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] OnVictory reward distribution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// After SwapToPlayer loads deck state, only shuffle if truly needed.
    /// LoadDeckState rebuilds battleDeck and shuffledDeck from the saved state.
    /// If shuffledDeck is populated, the loaded order is authoritative — do NOT
    /// re-shuffle or the host-deterministic draw order will be lost.
    /// Only shuffle if both battleDeck and shuffledDeck are empty (no state was saved),
    /// or if battleDeck has orbs but shuffledDeck couldn't be rebuilt (name mismatch).
    /// </summary>
    private void EnsureBattleDeckPopulated(string context)
    {
        try
        {
            var dms = UnityEngine.Resources.FindObjectsOfTypeAll<DeckManager>();
            if (dms == null || dms.Length == 0) return;

            var dm = dms[0];
            bool hasBattle = dm.battleDeck != null && dm.battleDeck.Count > 0;
            bool hasShuffled = dm.shuffledDeck != null && dm.shuffledDeck.Count > 0;

            if (hasBattle && hasShuffled)
            {
                // Both populated from loaded state — do NOT re-shuffle
                return;
            }

            if (hasBattle && !hasShuffled)
            {
                // Battle deck loaded but shuffled order couldn't be rebuilt
                // (e.g. orb name matching failed). Re-shuffle from the loaded battle deck.
                _log.LogInfo($"[CoopSubs] {context}: battleDeck has {dm.battleDeck.Count} orbs but shuffledDeck empty — re-shuffling");
                dm.ShuffleBattleDeck();
            }
            else if (!hasBattle && DeckManager.completeDeck != null && DeckManager.completeDeck.Count > 0)
            {
                // No battle deck loaded but complete deck exists — initialize battle deck.
                // CRITICAL: ShuffleBattleDeck() calls ShuffleCompleteDeck(fromComplete: false)
                // which reads from the EMPTY battleDeck and does nothing.
                // We must call ShuffleCompleteDeck(fromComplete: true) to build battleDeck
                // from completeDeck and populate shuffledDeck.
                _log.LogInfo($"[CoopSubs] {context}: battleDeck empty, initializing from completeDeck ({DeckManager.completeDeck.Count} orbs)");

                var shuffleMethod = AccessTools.Method(typeof(DeckManager), "ShuffleCompleteDeck", new[] { typeof(bool) });
                if (shuffleMethod != null)
                {
                    shuffleMethod.Invoke(dm, new object[] { true });
                    _log.LogInfo($"[CoopSubs] {context}: after ShuffleCompleteDeck(true): battleDeck={dm.battleDeck?.Count ?? 0}, shuffledDeck={dm.shuffledDeck?.Count ?? 0}");
                }
                else
                {
                    // Fallback: manually copy completeDeck into battleDeck, then shuffle
                    _log.LogWarning($"[CoopSubs] {context}: ShuffleCompleteDeck not found, manually copying completeDeck to battleDeck");
                    if (dm.battleDeck == null)
                        dm.battleDeck = new System.Collections.Generic.List<GameObject>();
                    foreach (var orb in DeckManager.completeDeck)
                    {
                        if (orb != null)
                        {
                            var instance = UnityEngine.Object.Instantiate(orb);
                            instance.name = orb.name;
                            instance.SetActive(false);
                            dm.battleDeck.Add(instance);
                        }
                    }
                    dm.ShuffleBattleDeck();
                    _log.LogInfo($"[CoopSubs] {context}: after fallback shuffle: battleDeck={dm.battleDeck?.Count ?? 0}, shuffledDeck={dm.shuffledDeck?.Count ?? 0}");
                }
            }
            // else: both empty — nothing to do, likely pre-battle state
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[CoopSubs] EnsureBattleDeckPopulated ({context}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Broadcast the current turn state to all clients.
    /// </summary>
    private void BroadcastTurnChange()
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var eventRegistry) != true) return;

            eventRegistry.Dispatch(new TurnChangeEvent
            {
                ActiveSlotIndex = _turnManager.CurrentPlayerSlot,
                ActivePlayerName = _turnManager.GetCurrentPlayerName(),
                TurnPhase = _turnManager.Phase.ToString(),
                RoundNumber = _turnManager.RoundNumber,
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] BroadcastTurnChange failed: {ex.Message}");
        }
    }
}
