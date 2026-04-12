using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Handlers.Coop;
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

        // Per-player targeting data
        public long PrecomputedDamage;
        public string TargetEnemyGuid;
        public bool IsAoE;      // SimpleAttack hits all enemies
        public bool IsHeal;     // HealAction — not an attack
        public string PlayerName;
    }

    private readonly IMultiplayerMode _mode;
    private readonly CoopStateManager _coopStateManager;
    private readonly TurnManager _turnManager;
    private readonly GameState.IGameStateSyncService _syncService;
    private readonly ManualLogSource _log;

    private readonly Dictionary<int, ShotDamageData> _accumulatedShotData = new Dictionary<int, ShotDamageData>();

    /// <summary>
    /// Stores the target GUID from the last pending shot (client → host).
    /// Set by ExecutePendingShot before consuming the pending shot.
    /// Read by OnShotComplete to record per-player targeting.
    /// </summary>
    internal static string LastPendingShotTargetGuid { get; set; }

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

    public CoopSubscriptions(IMultiplayerMode mode, CoopStateManager coopStateManager, TurnManager turnManager, GameState.IGameStateSyncService syncService, ManualLogSource log)
    {
        _mode = mode;
        _coopStateManager = coopStateManager;
        _turnManager = turnManager;
        _syncService = syncService;
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
            // --- Distribute enemy damage to all players ---
            var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (phc != null)
            {
                float currentHealth = phc.CurrentHealth;
                float damage = _healthBeforeAttack - currentHealth;

                if (damage > 0f)
                {
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
                        if (currentHealth > 0f)
                        {
                            _log.LogInfo("[CoopSubs] Active player alive but another player died. " +
                                "Setting active player health to 0 to trigger game over.");
                            phc.Damage(currentHealth, false);
                        }
                    }
                }
            }

            // --- Swap to host for next round's DrawBall ---
            // OnTurnComplete fires from DoEndOfTurnBattleCleanup, which runs RIGHT BEFORE
            // ChooseShuffleOrDrawAtEndOfTurn → DrawBall. By swapping to the host now,
            // DrawBall will use the host's deck for round 2. This avoids the broken
            // DOTween flow that occurred when we tried to call DrawBall manually later.
            if (_turnManager.Phase == TurnPhase.ALL_DONE)
            {
                _coopStateManager.SwapToPlayer(0);
                // Do NOT call EnsureBattleDeckPopulated here. If the host's
                // shuffledDeck is empty (all orbs fired), the game's native
                // ChooseShuffleOrDrawAtEndOfTurn → ShuffleBattleDeck →
                // onDeckShuffled → StartShuffleAnimation → PlungerPlungeComplete
                // handles the reshuffle with proper animation timing. Calling
                // EnsureBattleDeckPopulated would trigger the reshuffle too
                // early (before DrawBall), causing DrawNextOrb to pop from
                // _displayOrbs before PlungerPlungeComplete rebuilds them.
                //
                // DO rebuild the deck tube display though. LoadDeckState creates
                // new shuffledDeck object instances, so _displayOrbs is stale
                // (still referencing destroyed objects from before the swap).
                // Without this, the deck tube visually freezes after round 1.
                _coopStateManager.RebuildDeckInfoDisplay();
                _log.LogInfo($"[CoopSubs] Post-attack: swapped to host (slot 0) for next round's DrawBall");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] OnTurnComplete failed: {ex.Message}\n{ex.StackTrace}");
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
        PeglinMods.Multiplayer.UI.PendingDamageOverlay.ClearAll();
        _turnManager.BuildTurnOrder();
        _turnManager.StartNewRound();
        BroadcastTurnChange();

        _log.LogInfo($"[CoopSubs] Battle started — turn order built, round 1 started, slot {_turnManager.CurrentPlayerSlot} is up");

        // Re-capture host state from singletons so deck/relics reflect any
        // changes made between battles (rewards, new orbs, relic picks).
        // Without this, CoopPlayerState for the host holds stale data from
        // the previous battle and heartbeat sends empty decks.
        try
        {
            // The host is ALWAYS slot 0. Force swap before saving so we
            // don't accidentally overwrite the client's CoopPlayerState
            // with the host's singleton data (the singletons always hold
            // the host's data between battles).
            if (_coopStateManager.ActivePlayerSlot != 0)
            {
                _log.LogWarning($"[CoopSubs] Battle init: ActivePlayerSlot was {_coopStateManager.ActivePlayerSlot}, forcing swap to host (slot 0)");
                _coopStateManager.SwapToPlayer(0);
            }

            _coopStateManager.SaveActivePlayerState();
            _log.LogInfo("[CoopSubs] Battle init: saved host (slot 0) state from singletons");

            // Rebuild BattleDeck/ShuffledOrder from CompleteDeck for every
            // non-host player at the start of each battle. The saved state from
            // the previous battle has stale ShuffledOrder (only the remaining
            // draws, not the full deck). Without clearing first, the heartbeat
            // sends stale deck data until the host swaps to this player's turn,
            // causing the client deck UI to show the wrong number of orbs.
            foreach (var kvp in _coopStateManager.PlayerStates)
            {
                if (kvp.Key == 0) continue; // Always skip host (slot 0)
                var state = kvp.Value;
                if (state.CompleteDeck.Count > 0)
                {
                    // Clear stale battle state so LoadDeckState + EnsureBattleDeckPopulated
                    // rebuilds everything fresh from CompleteDeck
                    state.BattleDeck.Clear();
                    state.ShuffledOrder?.Clear();
                    _log.LogInfo($"[CoopSubs] Battle init: slot {kvp.Key} — rebuilding deck from completeDeck ({state.CompleteDeck.Count})");
                    _coopStateManager.SwapToPlayer(kvp.Key);
                    EnsureBattleDeckPopulated($"battle init slot {kvp.Key}");
                    _coopStateManager.SaveActivePlayerState();
                }
            }

            // Always swap back to host (slot 0) for the first turn
            if (_coopStateManager.ActivePlayerSlot != 0)
            {
                _coopStateManager.SwapToPlayer(0);
                _log.LogInfo("[CoopSubs] Battle init: swapped back to host (slot 0)");
            }

            // Send an immediate SyncAll so the client gets the correctly populated
            // AllDecks BEFORE the first round starts. Without this, the first SyncAll
            // (from StateSyncSubscriptions.OnBattleStarted) fires BEFORE we populate
            // the client's BattleDeck, so the client receives empty deck data.
            try
            {
                _syncService.SyncAll("BattleInit-DeckPopulated");
                _log.LogInfo("[CoopSubs] Battle init: sent immediate SyncAll with populated decks");
            }
            catch (System.Exception syncEx)
            {
                _log.LogWarning($"[CoopSubs] Battle init SyncAll failed: {syncEx.Message}");
            }
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[CoopSubs] Battle init state capture failed: {ex.Message}\n{ex.StackTrace}");
        }
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
        // The host's deck was already swapped in OnTurnComplete (before DrawBall),
        // so the game's natural DrawBall → DOTween → ArmBallForShot flow works.
        if (_turnManager.Phase == TurnPhase.ALL_DONE || _turnManager.Phase == TurnPhase.DAMAGE_PHASE)
        {
            _turnManager.StartNewRound();
            _log.LogInfo($"[CoopSubs] New round {_turnManager.RoundNumber} started, slot {_turnManager.CurrentPlayerSlot}");
        }
        BroadcastTurnChange();
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

            // Capture per-player targeting data while their state is still loaded
            var dmgMultField = AccessTools.Field(typeof(BattleController), "_damageMultiplier");
            var dmgBonusField = AccessTools.Field(typeof(BattleController), "_damageBonus");
            float dmgMult = dmgMultField != null ? (float)dmgMultField.GetValue(bc) : 1f;
            long dmgBonus = dmgBonusField != null ? (int)dmgBonusField.GetValue(bc) : 0;

            // Pre-compute this player's damage using the currently loaded Attack
            var amField = AccessTools.Field(typeof(BattleController), "_attackManager");
            var am = amField?.GetValue(bc) as Battle.Attacks.AttackManager;
            long precomputedDamage = 0;
            bool isAoE = false;
            bool isHeal = false;
            if (am != null)
            {
                precomputedDamage = am.GetCurrentDamage(pegTally, dmgMult, dmgBonus, critCount);
                isHeal = am.isHeal;

                // Check attack type to determine targeting behavior
                var attackField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
                var attack = attackField?.GetValue(am) as Battle.Attacks.Attack;
                if (attack is Battle.Attacks.SimpleAttack)
                    isAoE = true;
            }

            // Get target enemy GUID: host uses TargetingManager, client uses stored value
            string targetGuid = null;
            if (activeSlot == 0) // Host
            {
                var tmgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
                if (tmgr?.currentTarget != null)
                {
                    var services = MultiplayerPlugin.Services;
                    if (services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true)
                        targetGuid = eid.GetGuid(tmgr.currentTarget);
                }
            }
            else // Client — read from stored pending shot data
            {
                targetGuid = LastPendingShotTargetGuid;
                LastPendingShotTargetGuid = null;
            }

            // Resolve player name
            string playerName = null;
            if (_coopStateManager.PlayerStates.TryGetValue(activeSlot, out var pState))
                playerName = pState.PlayerName;

            _accumulatedShotData[activeSlot] = new ShotDamageData
            {
                PegMultiplierDamageTally = pegTally,
                CriticalHitCount = critCount,
                NumPegsHit = numPegs,
                CactusDamageTally = cactusTally,
                PrecomputedDamage = precomputedDamage,
                TargetEnemyGuid = targetGuid,
                IsAoE = isAoE,
                IsHeal = isHeal,
                PlayerName = playerName ?? $"Slot {activeSlot}",
            };

            _log.LogInfo($"[CoopSubs] Saved shot data for slot {activeSlot}: " +
                $"pegTally={pegTally}, crits={critCount}, pegsHit={numPegs}, " +
                $"damage={precomputedDamage}, target={targetGuid ?? "auto"}, isAoE={isAoE}");
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

            // Disconnect DeckInfoManager callbacks BEFORE EnsureBattleDeckPopulated.
            // If the non-host player's deck needs reshuffling,
            // ShuffleBattleDeck fires onDeckShuffled which would trigger the
            // host's DeckInfoManager animation and corrupt the host's deck
            // tube display. By disconnecting first, the reshuffle is silent.
            DeckManager.BallDrawn savedOnBallUsed = null;
            DeckManager.Shuffled savedOnDeckShuffled = null;
            try
            {
                savedOnBallUsed = DeckManager.onBallUsed;
                savedOnDeckShuffled = DeckManager.onDeckShuffled;
                DeckManager.onBallUsed = _ => { }; // no-op during non-host operations
                DeckManager.onDeckShuffled = _ => { }; // no-op during non-host shuffle
            }
            catch (Exception cbEx) { _log.LogWarning($"[CoopSubs] Failed to disconnect DeckManager callbacks: {cbEx.Message}"); }

            if (!EnsureBattleDeckPopulated("shot complete swap"))
                _log.LogWarning("[CoopSubs] EnsureBattleDeckPopulated failed after shot complete swap — deck may be empty");

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
                _log.LogWarning($"[CoopSubs] DrawBall call failed: {ex.InnerException?.Message ?? ex.Message}\n{ex.InnerException?.StackTrace ?? ex.StackTrace}");
            }
            finally
            {
                // Restore DeckInfoManager's callbacks so the host's own
                // deck tube continues to work normally for host shots.
                try
                {
                    if (savedOnBallUsed != null) DeckManager.onBallUsed = savedOnBallUsed;
                    if (savedOnDeckShuffled != null) DeckManager.onDeckShuffled = savedOnDeckShuffled;
                }
                catch (Exception restoreEx) { _log.LogWarning($"[CoopSubs] Failed to restore DeckManager callbacks: {restoreEx.Message}"); }
            }

            // Do NOT call RebuildDeckInfoDisplay here. The host should always
            // see the HOST's own deck in the deck tube UI, not the swapped-in
            // player's deck. DeckManager callbacks are disconnected above, so
            // the client's DrawBall won't touch the host's displayOrbs. The
            // native game flow (PlungerPlungeComplete + DrawNextOrb) keeps the
            // host's deck tube correct across rounds.

            BroadcastTurnChange();

            // Push updated AllDecks to client immediately so the client
            // sees its own deck rather than waiting for the next heartbeat.
            try { _syncService.SyncAll("TurnSwap"); }
            catch (Exception syncEx) { _log.LogWarning($"[CoopSubs] TurnSwap SyncAll failed: {syncEx.Message}"); }

            _log.LogInfo($"[CoopSubs] Swapped to player slot {_turnManager.CurrentPlayerSlot}, " +
                $"redirected BattleState -> AWAITING_SHOT");
        }
        else if (_turnManager.Phase == TurnPhase.ALL_DONE)
        {
            // All players have shot. Write only the HOST's (slot 0) tallies to
            // BattleController so the normal attack phase resolves the host's attack.
            // Non-host players' damage will be applied directly in the DoAttack prefix
            // based on their per-player targeting data in _accumulatedShotData.

            if (_accumulatedShotData.TryGetValue(0, out var hostData) && bc != null)
            {
                pegTallyField?.SetValue(bc, hostData.PegMultiplierDamageTally);
                critField?.SetValue(null, hostData.CriticalHitCount);
                numPegsField?.SetValue(bc, hostData.NumPegsHit);
                cactusField?.SetValue(bc, hostData.CactusDamageTally);
                _log.LogInfo($"[CoopSubs] Host (slot 0) damage: pegTally={hostData.PegMultiplierDamageTally}, " +
                    $"crits={hostData.CriticalHitCount}, pegsHit={hostData.NumPegsHit}");
            }
            else if (bc != null)
            {
                // Host data missing — zero out tallies to avoid stale data
                pegTallyField?.SetValue(bc, 0);
                critField?.SetValue(null, 0);
                numPegsField?.SetValue(bc, 0);
                cactusField?.SetValue(bc, 0);
                _log.LogWarning("[CoopSubs] Host slot 0 not found in accumulated shot data!");
            }

            // Set the host's target in TargetingManager for the native DoAttack
            if (_accumulatedShotData.TryGetValue(0, out var hostShot) && !string.IsNullOrEmpty(hostShot.TargetEnemyGuid))
            {
                try
                {
                    var services = MultiplayerPlugin.Services;
                    if (services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true)
                    {
                        var hostTarget = eid.Find(hostShot.TargetEnemyGuid);
                        if (hostTarget != null)
                        {
                            var tmgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
                            tmgr?.SetEnemyAsTarget(hostTarget, force: true);
                        }
                    }
                }
                catch (Exception ex) { _log.LogWarning($"[CoopSubs] Failed to set host target: {ex.Message}"); }
            }

            // Swap to host for the attack phase (their orb/relics drive the native attack)
            if (_coopStateManager.ActivePlayerSlot != 0)
            {
                _coopStateManager.SwapToPlayer(0);
                _log.LogInfo("[CoopSubs] ALL_DONE: swapped to host (slot 0) for attack phase");
            }

            // DO NOT clear _accumulatedShotData — the DoAttack prefix reads it
            // to apply non-host players' damage to their chosen targets.
            // The live PendingDamageOverlay was already updating during shots
            // via HandlePegActivated postfix — no final preview call needed.

            // DO NOT redirect state — let PRE_ATTACK_SPAWN_CHECK -> DO_ATTACK proceed normally
            BroadcastTurnChange();

            _log.LogInfo($"[CoopSubs] All players shot — per-player targeting active. Phase={_turnManager.Phase}");
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
            // Swap back to host BEFORE reward selection. OnVictory fires BEFORE
            // OnTurnComplete, so ActivePlayerSlot may still point to the last
            // client who shot. Without this swap:
            // - Host's orb/reward choices modify the client's loaded singletons
            // - SaveActivePlayerState reads stale singleton values, overwriting
            //   any CoopPlayerState changes (like client's +5 max HP reward)
            if (_coopStateManager.ActivePlayerSlot != 0)
            {
                _coopStateManager.SwapToPlayer(0);
                _log.LogInfo("[CoopSubs] OnVictory: swapped to host (slot 0) before reward selection");
            }
            _coopStateManager.SaveActivePlayerState();

            // Signal clients to open their native post-battle reward screen
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var registry) != true) return;

            // Clear previous reward tracking state
            CoopRewardState.PendingSentRewardChoices.Clear();
            CoopRewardState.ClientRewardChoicesReceived.Clear();
            CoopRewardState.HostRewardPhaseActive = true;
            CoopRewardState.HostRewardsDone = false;
            CoopRewardState.PendingPostBattleController = null;

            int rewardClientCount = 0;
            foreach (var kvp in _coopStateManager.PlayerStates)
            {
                if (kvp.Key == 0) continue; // Host picks rewards via the normal game UI
                rewardClientCount++;
            }

            CoopRewardState.TotalRewardClientsExpected = rewardClientCount;

            // Dispatch PostBattleStartEvent — clients will activate their own PostBattleController
            registry.Dispatch(new PostBattleStartEvent());

            _log.LogInfo($"[CoopSubs] Sent PostBattleStartEvent, expecting {rewardClientCount} client(s) to complete rewards");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] OnVictory reward distribution failed: {ex.Message}");
        }
    }

    // =========================================================================
    // PLAYER DISCONNECT — remove disconnected player from turn system
    // =========================================================================

    /// <summary>
    /// Handle a player disconnecting mid-battle. Removes them from the turn order
    /// and advances the turn if it was their turn. If all non-host players disconnect,
    /// the host continues solo.
    /// </summary>
    public void HandlePlayerDisconnect(int slotIndex, string playerName)
    {
        if (!_mode.IsHosting) return;

        _log.LogInfo($"[CoopSubs] HandlePlayerDisconnect: slot {slotIndex} ({playerName}) — " +
            $"turnPhase={_turnManager.Phase}, activeSlot={_turnManager.CurrentPlayerSlot}, " +
            $"totalPlayers={_coopStateManager.TotalPlayerCount}");

        // Remove from accumulated shot data
        _accumulatedShotData.Remove(slotIndex);

        // Remove from CoopStateManager
        if (_coopStateManager.PlayerStates.ContainsKey(slotIndex))
        {
            _coopStateManager.PlayerStates.Remove(slotIndex);
            _log.LogInfo($"[CoopSubs] Removed slot {slotIndex} from CoopStateManager. Remaining players: {_coopStateManager.TotalPlayerCount}");
        }

        // Remove from TurnManager — this tells us if it was their turn
        bool wasTheirTurn = _turnManager.RemovePlayer(slotIndex);

        // If only 1 player remains (the host), log that we're continuing solo
        if (_coopStateManager.TotalPlayerCount <= 1)
        {
            _log.LogInfo("[CoopSubs] All non-host players disconnected. Host continues solo.");
        }

        if (!wasTheirTurn)
        {
            // It wasn't their turn — turn order was adjusted, broadcast updated state
            BroadcastTurnChange();
            _log.LogInfo($"[CoopSubs] Disconnect handled (not their turn). Current turn: slot {_turnManager.CurrentPlayerSlot}");
            return;
        }

        // It was their turn — we need to handle the state machine transition.
        // The TurnManager already advanced to the next player or ALL_DONE.
        if (_turnManager.Phase == TurnPhase.ALL_DONE)
        {
            // All remaining players have shot. Write host tallies and let per-player
            // resolution handle the rest in the DoAttack prefix.
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            var pegTallyField = AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally");
            var critField = AccessTools.Field(typeof(BattleController), "_criticalHitCount");
            var numPegsField = AccessTools.Field(typeof(BattleController), "_numPegsHit");
            var cactusField = AccessTools.Field(typeof(BattleController), "_cactusDamageTally");

            if (_accumulatedShotData.TryGetValue(0, out var hostData) && bc != null)
            {
                pegTallyField?.SetValue(bc, hostData.PegMultiplierDamageTally);
                critField?.SetValue(null, hostData.CriticalHitCount);
                numPegsField?.SetValue(bc, hostData.NumPegsHit);
                cactusField?.SetValue(bc, hostData.CactusDamageTally);
            }

            if (_coopStateManager.ActivePlayerSlot != 0)
                _coopStateManager.SwapToPlayer(0);

            BroadcastTurnChange();
            _log.LogInfo($"[CoopSubs] Disconnect during turn: all remaining players done, per-player resolution active");
        }
        else if (_turnManager.Phase == TurnPhase.PLAYER_AIMING)
        {
            // Next player needs to shoot. Swap to the next player's state and
            // redirect BattleController back to AWAITING_SHOT.
            int nextSlot = _turnManager.CurrentPlayerSlot;

            _coopStateManager.SwapToPlayer(nextSlot);

            // Disconnect DeckInfoManager callbacks to avoid corrupting host UI
            DeckManager.BallDrawn savedOnBallUsed = null;
            DeckManager.Shuffled savedOnDeckShuffled = null;
            try
            {
                savedOnBallUsed = DeckManager.onBallUsed;
                savedOnDeckShuffled = DeckManager.onDeckShuffled;
                DeckManager.onBallUsed = _ => { };
                DeckManager.onDeckShuffled = _ => { };
            }
            catch (Exception cbEx) { _log.LogWarning($"[CoopSubs] Disconnect: failed to save DeckManager callbacks: {cbEx.Message}"); }

            EnsureBattleDeckPopulated("disconnect swap");

            // Redirect battle state back to AWAITING_SHOT
            BattleController.CurrentBattleState = BattleController.BattleState.AWAITING_SHOT;

            // Manually trigger DrawBall for the next player
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            try
            {
                if (bc != null)
                {
                    var drawBallMethod = AccessTools.Method(typeof(BattleController), "DrawBall");
                    drawBallMethod?.Invoke(bc, null);
                    _log.LogInfo($"[CoopSubs] Disconnect: called DrawBall for next player slot {nextSlot}");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[CoopSubs] Disconnect DrawBall failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            finally
            {
                try
                {
                    if (savedOnBallUsed != null) DeckManager.onBallUsed = savedOnBallUsed;
                    if (savedOnDeckShuffled != null) DeckManager.onDeckShuffled = savedOnDeckShuffled;
                }
                catch (Exception restoreEx) { _log.LogWarning($"[CoopSubs] Disconnect: failed to restore DeckManager callbacks: {restoreEx.Message}"); }
            }

            BroadcastTurnChange();

            try { _syncService.SyncAll("DisconnectTurnSwap"); }
            catch (Exception syncEx) { _log.LogWarning($"[CoopSubs] Disconnect SyncAll failed: {syncEx.Message}"); }

            _log.LogInfo($"[CoopSubs] Disconnect during turn: swapped to slot {nextSlot}, " +
                $"redirected BattleState -> AWAITING_SHOT");
        }
        else
        {
            // WAITING_FOR_PLAYERS or DAMAGE_PHASE — just broadcast the updated state
            BroadcastTurnChange();
            _log.LogInfo($"[CoopSubs] Disconnect handled in phase {_turnManager.Phase}");
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
    private bool EnsureBattleDeckPopulated(string context)
    {
        try
        {
            var dms = UnityEngine.Resources.FindObjectsOfTypeAll<DeckManager>();
            if (dms == null || dms.Length == 0) return false;

            var dm = dms[0];
            bool hasBattle = dm.battleDeck != null && dm.battleDeck.Count > 0;
            bool hasShuffled = dm.shuffledDeck != null && dm.shuffledDeck.Count > 0;

            if (hasBattle && hasShuffled)
            {
                // Both populated from loaded state — do NOT re-shuffle
                return true;
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

            // Return success: both battleDeck and shuffledDeck are non-empty
            bool success = (dm.battleDeck != null && dm.battleDeck.Count > 0) &&
                           (dm.shuffledDeck != null && dm.shuffledDeck.Count > 0);
            return success;
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[CoopSubs] EnsureBattleDeckPopulated ({context}) failed: {ex.Message}");
            return false;
        }
    }

    // =========================================================================
    // PER-PLAYER DAMAGE RESOLUTION — accessed by DoAttack Harmony prefix
    // =========================================================================

    /// <summary>
    /// Data for a single player's shot, consumed during DoAttack.
    /// </summary>
    public class PlayerAttackData
    {
        public int SlotIndex;
        public string PlayerName;
        public long Damage;
        public string TargetEnemyGuid;
        public bool IsAoE;
        public bool IsHeal;
    }

    /// <summary>
    /// Returns non-host players' accumulated shot data for per-player damage resolution.
    /// Called by the DoAttack Harmony prefix. Clears the data after consumption.
    /// </summary>
    internal static List<PlayerAttackData> ConsumeNonHostShotData()
    {
        var inst = Instance;
        if (inst == null) return null;

        var result = new List<PlayerAttackData>();
        foreach (var kvp in inst._accumulatedShotData)
        {
            if (kvp.Key == 0) continue; // Skip host — handled by normal DoAttack
            var d = kvp.Value;
            result.Add(new PlayerAttackData
            {
                SlotIndex = kvp.Key,
                PlayerName = d.PlayerName,
                Damage = d.PrecomputedDamage,
                TargetEnemyGuid = d.TargetEnemyGuid,
                IsAoE = d.IsAoE,
                IsHeal = d.IsHeal,
            });
        }
        inst._accumulatedShotData.Clear();
        return result;
    }

    /// <summary>
    /// Returns ALL players' accumulated shot data for the pending damage overlay.
    /// Does NOT clear the data — called repeatedly during shots.
    /// </summary>
    internal static List<Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry> GetAccumulatedDamageEntries()
    {
        var inst = Instance;
        if (inst == null) return null;

        var result = new List<Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry>();
        foreach (var kvp in inst._accumulatedShotData)
        {
            var d = kvp.Value;
            if (d.IsHeal || d.PrecomputedDamage <= 0) continue;
            result.Add(new Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry
            {
                SlotIndex = kvp.Key,
                PlayerName = d.PlayerName,
                Damage = d.PrecomputedDamage,
                TargetEnemyGuid = d.TargetEnemyGuid,
                IsAoE = d.IsAoE,
            });
        }
        return result;
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
