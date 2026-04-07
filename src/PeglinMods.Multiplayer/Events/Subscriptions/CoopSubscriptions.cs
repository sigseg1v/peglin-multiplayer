using System;
using System.Collections.Generic;
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
    /// <summary>Per-player peg damage data accumulated during shots.</summary>
    private class ShotDamageData
    {
        public int PegMultiplierDamageTally;
        public int CriticalHitCount;
        public int NumPegsHit;
        public int CactusDamageTally;
    }

    private readonly IMultiplayerMode _mode;
    private readonly CoopStateManager _coopStateManager;
    private readonly TurnManager _turnManager;
    private readonly ManualLogSource _log;

    private readonly Dictionary<int, ShotDamageData> _accumulatedShotData = new Dictionary<int, ShotDamageData>();

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

        _accumulatedShotData.Clear();
        _turnManager.BuildTurnOrder();
        _turnManager.StartNewRound();
        BroadcastTurnChange();

        _log.LogInfo($"[CoopSubs] Battle started — turn order built, round 1 started, slot {_turnManager.CurrentPlayerSlot} is up");
    }

    /// <summary>
    /// Called when the game is ready for a shot (awaiting player input).
    /// After a full turn cycle (all players shot + attack + enemy turn),
    /// the game naturally reaches AWAITING_SHOT. Start a new round.
    /// If we're already in PLAYER_AIMING, do nothing — OnShotComplete handles player swaps.
    /// </summary>
    private void OnAwaitingShot()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        // After a full turn cycle (all players shot + attack + enemy turn),
        // the game naturally reaches AWAITING_SHOT. Start a new round.
        if (_turnManager.Phase == TurnPhase.ALL_DONE || _turnManager.Phase == TurnPhase.DAMAGE_PHASE)
        {
            _turnManager.StartNewRound();
            if (_turnManager.CurrentPlayerSlot >= 0)
            {
                _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);
                EnsureBattleDeckPopulated("new round");

                // The DOTween → ArmBallForShot flow is broken after a deck swap.
                // Force DrawBall + fully initialize the ball for aiming.
                try
                {
                    var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
                    if (bc != null)
                    {
                        // Create ball from the swapped-in deck
                        var drawBallMethod = AccessTools.Method(typeof(Battle.BattleController), "DrawBall");
                        drawBallMethod?.Invoke(bc, null);

                        var activeBallField = AccessTools.Field(typeof(Battle.BattleController), "_activePachinkoBall");
                        var ballGO = activeBallField?.GetValue(bc) as UnityEngine.GameObject;
                        if (ballGO != null)
                        {
                            // Kill DOTween (it won't complete after swap) and snap scale
                            DG.Tweening.DOTween.Kill(ballGO.transform);
                            ballGO.transform.localScale = UnityEngine.Vector3.one * 0.32f;

                            var ball = ballGO.GetComponent<PachinkoBall>();
                            if (ball != null)
                            {
                                // InitializeMembers sets _player, _rigid, _mainCamera etc.
                                // Without this, PachinkoBall.Update() can't read mouse input.
                                ball.InitializeMembers();

                                // Set AIMING state (Arm() NREs on _predictionManager)
                                var stateProp = AccessTools.Property(typeof(PachinkoBall), "CurrentState");
                                stateProp?.GetSetMethod(true)?.Invoke(ball, new object[] { PachinkoBall.FireballState.AIMING });
                            }

                            // Ensure ball is active (same deactivation issue as client shots)
                            if (!ballGO.activeInHierarchy)
                                ballGO.SetActive(true);

                            _log.LogInfo($"[CoopSubs] DrawBall + Init + AIMING for round {_turnManager.RoundNumber}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[CoopSubs] Round swap ball setup failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
            BroadcastTurnChange();
        }
        // If we're already in PLAYER_AIMING, do nothing — OnShotComplete handles player swaps
    }

    /// <summary>
    /// Called when a shot resolves. Save the current player's peg damage tallies,
    /// then either advance to the next player (more shots needed) or let the
    /// attack proceed with combined damage from all players.
    /// </summary>
    private void OnShotComplete()
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        int activeSlot = _coopStateManager.ActivePlayerSlot;

        // Read BattleController's peg damage tallies for this player's shot
        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
        var pegTallyField = AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally");
        var critField = AccessTools.Field(typeof(BattleController), "_criticalHitCount");
        var numPegsField = AccessTools.Field(typeof(BattleController), "_numPegsHit");
        var cactusField = AccessTools.Field(typeof(BattleController), "_cactusDamageTally");

        if (bc != null)
        {
            int pegTally = pegTallyField != null ? (int)pegTallyField.GetValue(bc) : 0;
            // _criticalHitCount is static, so pass null for the instance
            int critCount = critField != null ? (int)critField.GetValue(null) : 0;
            int numPegs = numPegsField != null ? (int)numPegsField.GetValue(bc) : 0;
            int cactusTally = cactusField != null ? (int)cactusField.GetValue(bc) : 0;

            _accumulatedShotData[activeSlot] = new ShotDamageData
            {
                PegMultiplierDamageTally = pegTally,
                CriticalHitCount = critCount,
                NumPegsHit = numPegs,
                CactusDamageTally = cactusTally,
            };

            _log.LogInfo($"[CoopSubs] Saved shot data for slot {activeSlot}: pegTally={pegTally}, crits={critCount}, pegsHit={numPegs}, cactus={cactusTally}");
        }

        // Mark current player's shot as fired before advancing
        _turnManager.MarkShotFired();

        // Save current player's state before swapping
        _coopStateManager.SaveActivePlayerState();

        _turnManager.AdvanceTurn();

        if (_turnManager.Phase == TurnPhase.PLAYER_AIMING && _turnManager.CurrentPlayerSlot >= 0)
        {
            // More players need to shoot. Reset tallies to 0 for the next player,
            // swap to them, and redirect BattleController back to AWAITING_SHOT.

            if (bc != null)
            {
                pegTallyField?.SetValue(bc, 0);
                critField?.SetValue(null, 0); // static field
                numPegsField?.SetValue(bc, 0);
                cactusField?.SetValue(bc, 0);
            }

            _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);
            EnsureBattleDeckPopulated("shot complete swap");

            // Override BattleState back to AWAITING_SHOT so the state machine
            // re-enters the aiming phase instead of proceeding to attack.
            BattleController.CurrentBattleState = BattleController.BattleState.AWAITING_SHOT;

            // Manually trigger DrawBall since we bypassed the normal flow.
            try
            {
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
            catch (Exception ex)
            {
                _log.LogWarning($"[CoopSubs] DrawBall call failed: {ex.Message}");
            }

            BroadcastTurnChange();

            _log.LogInfo($"[CoopSubs] Swapped to player slot {_turnManager.CurrentPlayerSlot}, " +
                $"redirected BattleState -> AWAITING_SHOT");
        }
        else if (_turnManager.Phase == TurnPhase.ALL_DONE)
        {
            // All players have shot. Combine accumulated damage and write back
            // to BattleController so the normal attack phase uses the total.

            int totalPegTally = 0, totalCrits = 0, totalPegsHit = 0, totalCactus = 0;
            foreach (var data in _accumulatedShotData.Values)
            {
                totalPegTally += data.PegMultiplierDamageTally;
                totalCrits += data.CriticalHitCount;
                totalPegsHit += data.NumPegsHit;
                totalCactus += data.CactusDamageTally;
            }

            if (bc != null)
            {
                pegTallyField?.SetValue(bc, totalPegTally);
                critField?.SetValue(null, totalCrits); // static field
                numPegsField?.SetValue(bc, totalPegsHit);
                cactusField?.SetValue(bc, totalCactus);
            }

            _log.LogInfo($"[CoopSubs] Combined damage: pegTally={totalPegTally}, crits={totalCrits}, pegsHit={totalPegsHit}, cactus={totalCactus}");
            _accumulatedShotData.Clear();

            // DO NOT redirect state — let PRE_ATTACK_SPAWN_CHECK -> DO_ATTACK proceed normally
            BroadcastTurnChange();

            _log.LogInfo($"[CoopSubs] All players shot — letting attack phase proceed. Phase={_turnManager.Phase}");
        }
        else
        {
            BroadcastTurnChange();
            _log.LogInfo($"[CoopSubs] Shot complete — Phase={_turnManager.Phase}, slot={_turnManager.CurrentPlayerSlot}");
        }
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
