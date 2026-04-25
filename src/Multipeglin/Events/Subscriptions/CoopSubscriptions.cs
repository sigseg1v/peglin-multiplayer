using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions;

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
        public float DamageMultiplier;
        public long DamageBonus;

        // Per-player targeting data
        public long PrecomputedDamage;
        public string TargetEnemyGuid;
        public bool IsAoE;      // SimpleAttack hits all enemies
        public bool IsHeal;     // HealAction — not an attack
        public string PlayerName;

        // Status effects captured from the orb's IAffectEnemyOnHit components + relic effects
        public List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> StatusEffectsToApply;

        // Orb name (prefab name without "(Clone)") for re-instantiating at ALL_DONE.
        // Used with AssetLoading.GetOrbPrefab() to get the correct orb type so DoAttack
        // computes damage with the right formula.
        public string OrbPrefabName;

        // Pincer Maneuver (ADDITIONAL_REVERSE_PROJECTILE_ATTACK): when true, the primary
        // damage has already been halved (rounded up) and a second shot of the same
        // halved damage should land on the farthest enemy from the player.
        public bool HasReverseShot;
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
    /// Tracks damage that has already been distributed via OnPlayerDamaged
    /// (the reactive path) during the current attack window. OnTurnComplete
    /// uses this to avoid double-distributing the same damage.
    /// </summary>
    private float _damageDistributedSinceAttackStart;

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
                _damageDistributedSinceAttackStart = 0f;
                return;
            }

            _healthBeforeAttack = phc.CurrentHealth;
            _damageDistributedSinceAttackStart = 0f;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] OnAttackStarted failed: {ex.Message}");
            _healthBeforeAttack = 0f;
            _damageDistributedSinceAttackStart = 0f;
        }
    }

    /// <summary>
    /// Distribute damage to every non-active player's CoopPlayerState, applying
    /// their individual defensive buffs. Called from OnPlayerDamaged (reactive,
    /// catches every phc.Damage() source including red-bomb detonations) and
    /// from OnTurnComplete (residual reconciliation).
    /// </summary>
    public void DistributeDamageToNonActive(float rawDamage, string source)
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;
        if (rawDamage <= 0f) return;

        int activeSlot = _coopStateManager.ActivePlayerSlot;
        foreach (var state in _coopStateManager.PlayerStates.Values)
        {
            if (state.SlotIndex == activeSlot) continue;

            float oldHp = state.CurrentHealth;
            float effectiveDamage = ApplyDefensiveBuffs(state, rawDamage);
            state.CurrentHealth = Mathf.Max(0f, state.CurrentHealth - effectiveDamage);
            float actualTaken = oldHp - state.CurrentHealth;
            if (actualTaken > 0) state.DamageTaken += (long)actualTaken;
            _log.LogInfo($"[CoopSubs] DistributeDamage({source}) slot {state.SlotIndex} ({state.PlayerName}): " +
                $"hp {oldHp} -> {state.CurrentHealth} (raw={rawDamage}, after buffs={effectiveDamage})");
        }
    }

    /// <summary>
    /// Called from HealthSubscriptions.OnPlayerDamaged on every phc.Damage() call.
    /// Distributes the damage immediately to non-active players so damage sources
    /// that bypass the OnAttackStarted/OnTurnComplete window (e.g. red bomb mid-turn)
    /// still hit every player.
    /// </summary>
    public void HandleImmediateDamage(float amount)
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;
        if (amount <= 0f) return;

        DistributeDamageToNonActive(amount, "OnPlayerDamaged");
        _damageDistributedSinceAttackStart += amount;

        // Also mirror the damage into the active player's CoopPlayerState so
        // TurnManager.StartNewRound can correctly auto-skip them if they just
        // died. Without this, state.CurrentHealth only refreshes on the next
        // SaveActivePlayerState (swap boundary) — which means a player who
        // dies mid-damage-phase still gets scheduled as round-start slot 0.
        int activeSlot = _coopStateManager.ActivePlayerSlot;
        if (activeSlot >= 0)
        {
            var activeState = _coopStateManager.GetPlayerState(activeSlot);
            if (activeState != null)
            {
                var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
                if (phc != null)
                {
                    activeState.CurrentHealth = Mathf.Max(0f, phc.CurrentHealth);
                }
                // Accumulate damage taken for the active slot's run-summary tally.
                if (amount > 0) activeState.DamageTaken += (long)amount;
            }
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
            // --- Reconcile any residual damage that wasn't distributed via OnPlayerDamaged ---
            // DistributeDamageToNonActive handles the common path (every phc.Damage() call).
            // But some damage sources (e.g. red bomb detonation mid-turn, delayed explosions)
            // can bypass the OnPlayerDamaged dispatch window. The _healthBeforeAttack delta
            // catches any leftover damage and distributes it the same way.
            var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
            if (phc != null)
            {
                float currentHealth = phc.CurrentHealth;
                float damage = _healthBeforeAttack - currentHealth - _damageDistributedSinceAttackStart;

                if (damage > 0f)
                {
                    _coopStateManager.SaveActivePlayerState();
                    DistributeDamageToNonActive(damage, "OnTurnComplete-residual");
                    _damageDistributedSinceAttackStart += damage;
                }

                // Update _healthBeforeAttack so that if OnTurnComplete fires again
                // from a board-reload extra enemy turn (_skipPlayerTurnCount > 0),
                // only the INCREMENTAL damage is reconciled on the next pass.
                _healthBeforeAttack = currentHealth;
                _damageDistributedSinceAttackStart = 0f;

                if (_coopStateManager.AllPlayersDead)
                {
                    _log.LogWarning("[CoopSubs] All co-op players have died! Triggering defeat.");
                    // phc.Damage(0) is a no-op, and by now every player's HP is already 0.
                    // CheckForDeathAndUpdateBar triggers GameOverObj.SetActive + OnHealthDepleted
                    // directly; the Harmony prefix in MultiplayerClientPatches lets it through
                    // because AllPlayersDead is true.
                    phc.CheckForDeathAndUpdateBar();
                }
                else if (_coopStateManager.AnyPlayerDead)
                {
                    _log.LogInfo("[CoopSubs] Some players dead, but not all — battle continues. Dead players will skip turns.");
                }
            }

            // --- Swap to the slot that will shoot first next round ---
            // OnTurnComplete fires from DoEndOfTurnBattleCleanup, which runs RIGHT BEFORE
            // ChooseShuffleOrDrawAtEndOfTurn → DrawBall. The singletons must hold the
            // correct player's deck before DrawBall runs, otherwise DrawBall pops the
            // wrong player's orb AND DeckInfoManager.DrawNextOrb pops the stale display
            // stack — leaving the host's deck UI showing orbs that don't match the
            // actual active shooter's deck.
            //
            // Normally slot 0 (host) starts each round. But if the host is dead,
            // TurnManager.StartNewRound will skip slot 0 and pick the first live slot.
            // Pre-empt that decision here so the native DrawBall fires for the right
            // player and the display matches.
            if (_turnManager.Phase == TurnPhase.ALL_DONE)
            {
                int nextSlot = 0;
                foreach (var slot in _turnManager.TurnOrder)
                {
                    var st = _coopStateManager.GetPlayerState(slot);
                    if (st != null && st.CurrentHealth > 0) { nextSlot = slot; break; }
                }

                _coopStateManager.SwapToPlayer(nextSlot);
                // Do NOT call EnsureBattleDeckPopulated here. If the active
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
                _log.LogInfo($"[CoopSubs] Post-attack: swapped to slot {nextSlot} for next round's DrawBall");
            }

            // Clean up any temp orb instance created during ALL_DONE for attack restoration
            CleanupTempOrb();
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
        Multipeglin.UI.PendingDamageOverlay.ClearAll();
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
            _accumulatedShotData.Clear();
            Multipeglin.UI.PendingDamageOverlay.ClearAll();

            // Reset BattleController tallies so they don't carry over from the
            // previous round's ALL_DONE restoration (where host tallies were
            // written back for DoAttack). Without this, the next round's peg
            // hits accumulate on top of the stale values.
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            if (bc != null)
            {
                AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_criticalHitCount")?.SetValue(null, 0);
                AccessTools.Field(typeof(BattleController), "_numPegsHit")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_cactusDamageTally")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_damageMultiplier")?.SetValue(bc, 1f);
                AccessTools.Field(typeof(BattleController), "_damageBonus")?.SetValue(bc, 0);
            }

            // Send clear event to client so its overlay resets too
            try
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
                    reg.Dispatch(new Events.Network.Coop.PendingDamagePreviewEvent());
            }
            catch { }

            _turnManager.StartNewRound();
            _log.LogInfo($"[CoopSubs] New round {_turnManager.RoundNumber} started, slot {_turnManager.CurrentPlayerSlot}");

            // If the round starts with a non-host slot (host was dead so
            // StartNewRound skipped past slot 0), the host already drew a ball
            // for itself in OnTurnComplete's swap-to-host-for-DrawBall flow.
            // Swap state to the live slot and redraw from its deck so
            // ActivePlayerSlot matches the active turn and the client's
            // heartbeat-driven IsMyTurn flag doesn't get reset to False.
            if (_turnManager.Phase == TurnPhase.PLAYER_AIMING
                && _turnManager.CurrentPlayerSlot != _coopStateManager.ActivePlayerSlot)
            {
                SwapAndRedrawForRoundStart($"round {_turnManager.RoundNumber} start, slot 0 dead");
            }
        }
        BroadcastTurnChange();

        // Defensive: if the newly-active player is dead (e.g. a DoT killed them
        // between rounds and TurnManager's skip loop missed it for any reason),
        // auto-skip them to avoid softlock. Loops until we land on a live slot
        // or the whole round ends.
        SkipActiveSlotIfDead("OnAwaitingShot");
    }

    /// <summary>
    /// Called from OnAwaitingShot when a new round starts with a live slot that
    /// doesn't match the currently loaded ActivePlayerSlot (i.e. host was dead
    /// and StartNewRound skipped past it). Destroys the mis-drawn host ball,
    /// swaps singletons to the live slot, redraws from that slot's deck, and
    /// pushes a full SyncAll so the client sees its own deck immediately.
    /// Mirrors the "more players left" swap block in OnShotComplete.
    /// </summary>
    private void SwapAndRedrawForRoundStart(string context)
    {
        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();

        try
        {
            if (bc != null)
            {
                var activeBallField = AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                if (activeBallField?.GetValue(bc) is GameObject activeBall)
                {
                    UnityEngine.Object.Destroy(activeBall);
                    activeBallField.SetValue(bc, null);
                }
                AccessTools.Field(typeof(BattleController), "_remainingPachinkoBalls")?.SetValue(bc, 0);

                AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_criticalHitCount")?.SetValue(null, 0);
                AccessTools.Field(typeof(BattleController), "_numPegsHit")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_cactusDamageTally")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_damageMultiplier")?.SetValue(bc, 1f);
                AccessTools.Field(typeof(BattleController), "_damageBonus")?.SetValue(bc, 0);
            }
        }
        catch (Exception ex) { _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): pre-swap cleanup failed: {ex.Message}"); }

        _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);

        // Silence DeckInfoManager callbacks during the non-host shuffle/draw so
        // they don't corrupt the host's deck tube display (same guard as
        // OnShotComplete).
        DeckManager.BallDrawn savedOnBallUsed = null;
        DeckManager.Shuffled savedOnDeckShuffled = null;
        try
        {
            savedOnBallUsed = DeckManager.onBallUsed;
            savedOnDeckShuffled = DeckManager.onDeckShuffled;
            DeckManager.onBallUsed = _ => { };
            DeckManager.onDeckShuffled = _ => { };
        }
        catch (Exception cbEx) { _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): callback disconnect failed: {cbEx.Message}"); }

        if (!EnsureBattleDeckPopulated($"{context} swap"))
            _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): EnsureBattleDeckPopulated failed — deck may be empty");

        // Rebuild the deck tube display for the newly loaded slot BEFORE the
        // manual DrawBall. Otherwise _displayOrbs still holds stale orb instances
        // from the previous swap (created by the prior RebuildDeckInfoDisplay +
        // native DrawBall), and the host's deck UI shows those stale orbs piled
        // on top of each other while the active shooter is a different slot.
        _coopStateManager.RebuildDeckInfoDisplay();

        try
        {
            BattleController.CurrentBattleState = BattleController.BattleState.AWAITING_SHOT;
            if (bc != null)
            {
                var drawBallMethod = AccessTools.Method(typeof(BattleController), "DrawBall");
                drawBallMethod?.Invoke(bc, null);
                _log.LogInfo($"[CoopSubs] SwapAndRedraw ({context}): DrawBall invoked for slot {_turnManager.CurrentPlayerSlot}");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): DrawBall failed: {ex.InnerException?.Message ?? ex.Message}");
        }
        finally
        {
            try
            {
                if (savedOnBallUsed != null) DeckManager.onBallUsed = savedOnBallUsed;
                if (savedOnDeckShuffled != null) DeckManager.onDeckShuffled = savedOnDeckShuffled;
            }
            catch (Exception restoreEx) { _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): callback restore failed: {restoreEx.Message}"); }
        }

        try { _syncService.SyncAll($"{context}"); }
        catch (Exception syncEx) { _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): SyncAll failed: {syncEx.Message}"); }
    }

    /// <summary>
    /// If the active turn slot has 0 HP while Phase==PLAYER_AIMING, run
    /// SkipCurrentTurn. Repeats until a live player is current or the round ends.
    /// </summary>
    private void SkipActiveSlotIfDead(string reason)
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        int safety = 0;
        while (_turnManager.Phase == TurnPhase.PLAYER_AIMING && safety++ < 16)
        {
            int slot = _turnManager.CurrentPlayerSlot;
            if (slot < 0) break;
            var state = _coopStateManager.GetPlayerState(slot);
            if (state == null || !state.IsInitialized) break;
            if (state.CurrentHealth > 0) break;

            _log.LogInfo($"[CoopSubs] Auto-skip dead slot {slot} (hp=0) source={reason}");
            SkipCurrentTurn(slot, $"auto-skip dead ({reason})");
        }
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

        // BattleController.OnShotComplete also fires when bomb balls finish
        // during THROW_BOMBS. By then all players have shot (Phase == ALL_DONE)
        // and the ALL_DONE branch below has already written the host's tallies
        // to BC and swapped to host. Re-running would overwrite the saved shot
        // data for slot 0 with post-bomb BC state and re-enter the ALL_DONE
        // branch — harmless on its own, but it re-writes host tallies into
        // BC which can replay as double damage if DoAttack re-enters.
        if (_turnManager.Phase == TurnPhase.ALL_DONE)
        {
            _log.LogInfo("[CoopSubs] OnShotComplete re-entry in ALL_DONE (bomb ball) — skipping");
            return;
        }

        int activeSlot = _coopStateManager.ActivePlayerSlot;

        // Read BattleController's peg damage tallies for this player's shot
        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
        var pegTallyField = AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally");
        var critField = AccessTools.Field(typeof(BattleController), "_criticalHitCount");
        var numPegsField = AccessTools.Field(typeof(BattleController), "_numPegsHit");
        var cactusField = AccessTools.Field(typeof(BattleController), "_cactusDamageTally");
        var dmgMultField = AccessTools.Field(typeof(BattleController), "_damageMultiplier");
        var dmgBonusField = AccessTools.Field(typeof(BattleController), "_damageBonus");

        if (bc != null)
        {
            int pegTally = pegTallyField != null ? (int)pegTallyField.GetValue(bc) : 0;
            // _criticalHitCount is static, so pass null for the instance
            int critCount = critField != null ? (int)critField.GetValue(null) : 0;
            int numPegs = numPegsField != null ? (int)numPegsField.GetValue(bc) : 0;
            int cactusTally = cactusField != null ? (int)cactusField.GetValue(bc) : 0;

            float dmgMult = dmgMultField != null ? (float)dmgMultField.GetValue(bc) : 1f;
            long dmgBonus = dmgBonusField != null ? (int)dmgBonusField.GetValue(bc) : 0;

            // Pre-compute this player's damage using the currently loaded Attack
            var amField = AccessTools.Field(typeof(BattleController), "_attackManager");
            var am = amField?.GetValue(bc) as Battle.Attacks.AttackManager;
            long precomputedDamage = 0;
            bool isAoE = false;
            bool isHeal = false;
            List<(Battle.StatusEffects.StatusEffectType, int)> capturedEffects = null;
            string capturedOrbName = null;
            // Pincer Maneuver (ADDITIONAL_REVERSE_PROJECTILE_ATTACK): when the active
            // player has this relic, ProjectileAttack.Fire halves the damage (rounded
            // up) and schedules a second shot at the farthest enemy with the same
            // halved value. We bypass Fire in coop, so replicate the split here.
            bool hasReverseShot = false;
            {
                var rms = Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                var rm = rms != null && rms.Length > 0 ? rms[0] : null;
                if (rm != null)
                    hasReverseShot = rm.RelicEffectActive(Relics.RelicEffect.ADDITIONAL_REVERSE_PROJECTILE_ATTACK);
            }

            if (am != null)
            {
                precomputedDamage = am.GetCurrentDamage(pegTally, dmgMult, dmgBonus, critCount);
                isHeal = am.isHeal;
                if (hasReverseShot && !isHeal && precomputedDamage > 0)
                {
                    long remainder = precomputedDamage % 2;
                    precomputedDamage = precomputedDamage / 2 + remainder;
                }

                // Check attack type to determine targeting behavior
                var attackField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
                var attack = attackField?.GetValue(am) as Battle.Attacks.Attack;
                if (attack is Battle.Attacks.SimpleAttack)
                    isAoE = true;

                // Capture status effects from the orb's components for non-host players.
                // The host's effects are applied by the normal attack pipeline in DoAttack,
                // but non-host damage is applied via enemy.Damage() which skips status effects.
                if (activeSlot != 0 && attack != null)
                {
                    capturedEffects = CaptureOrbStatusEffects(attack, critCount);
                }

                // Apply self-granting post-attack status effects (Ballusion from Evasive
                // Maneuvorb, Muscircle from Strongball, etc.) to the CURRENTLY LOADED
                // player's PlayerStatusEffectController. We do this for ALL slots (host
                // included) because the DoAttack Harmony prefix skips the native attack
                // pipeline in coop mode, so CallPostAttackOperations never runs.
                //
                // Timing note: SaveActivePlayerState() is called immediately after this
                // block, so these effects are persisted into CoopPlayerState.StatusEffects
                // for the current shooter before we swap to the next player.
                if (attack != null)
                {
                    ApplySelfPostAttackBuffs(attack, critCount, activeSlot);
                }

                // Capture the orb name for re-instantiation at ALL_DONE.
                // The active ball is still alive here (not destroyed until DrawBall for next player).
                // We store the prefab name so we can look it up via AssetLoading.GetOrbPrefab().
                try
                {
                    var activeBallField = AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                    var activeBall = activeBallField?.GetValue(bc) as GameObject;
                    if (activeBall != null)
                    {
                        capturedOrbName = activeBall.name.Replace("(Clone)", "").Trim();
                    }
                    else
                    {
                        _log.LogWarning($"[CoopSubs] _activePachinkoBall was null at shot capture for slot {activeSlot}");
                    }
                }
                catch (Exception ex) { _log.LogWarning($"[CoopSubs] Failed to capture orb name: {ex.Message}"); }
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
                DamageMultiplier = dmgMult,
                DamageBonus = dmgBonus,
                PrecomputedDamage = precomputedDamage,
                TargetEnemyGuid = targetGuid,
                IsAoE = isAoE,
                IsHeal = isHeal,
                PlayerName = playerName ?? $"Slot {activeSlot}",
                StatusEffectsToApply = capturedEffects,
                OrbPrefabName = capturedOrbName,
                HasReverseShot = hasReverseShot && !isHeal && precomputedDamage > 0,
            };

            _log.LogInfo($"[CoopSubs] Saved shot data for slot {activeSlot}: " +
                $"pegTally={pegTally}, crits={critCount}, pegsHit={numPegs}, " +
                $"damage={precomputedDamage}, target={targetGuid ?? "auto"}, isAoE={isAoE}, " +
                $"statusEffects={capturedEffects?.Count ?? 0}, orb={capturedOrbName ?? "NONE"}");
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
                dmgMultField?.SetValue(bc, 1f);
                dmgBonusField?.SetValue(bc, 0);
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
                dmgMultField?.SetValue(bc, hostData.DamageMultiplier);
                dmgBonusField?.SetValue(bc, (int)hostData.DamageBonus);
                _log.LogInfo($"[CoopSubs] Host (slot 0) damage: pegTally={hostData.PegMultiplierDamageTally}, " +
                    $"crits={hostData.CriticalHitCount}, pegsHit={hostData.NumPegsHit}, " +
                    $"dmgMult={hostData.DamageMultiplier}, dmgBonus={hostData.DamageBonus}");
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

            // Restore the host's Attack into AttackManager._attack.
            // The last DrawBall was for the last non-host player, so _attack points to
            // their orb. DoAttack calls _attack.Fire() which uses the wrong damage formula
            // unless we restore the host's orb here.
            if (_accumulatedShotData.TryGetValue(0, out var hostAttackData) && !string.IsNullOrEmpty(hostAttackData.OrbPrefabName) && bc != null)
            {
                try
                {
                    var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(hostAttackData.OrbPrefabName);
                    if (orbPrefab != null)
                    {
                        RestoreAttackFromPrefab(bc, orbPrefab, "host");
                    }
                    else
                    {
                        _log.LogWarning($"[CoopSubs] ALL_DONE: AssetLoading.GetOrbPrefab returned null for '{hostAttackData.OrbPrefabName}'");
                    }
                }
                catch (Exception ex) { _log.LogWarning($"[CoopSubs] Failed to restore host Attack: {ex.Message}"); }
            }
            else if (bc != null)
            {
                _log.LogWarning($"[CoopSubs] ALL_DONE: Cannot restore host Attack — " +
                    $"hasSlot0={_accumulatedShotData.ContainsKey(0)}, " +
                    $"orbName={(_accumulatedShotData.TryGetValue(0, out var d) ? d.OrbPrefabName ?? "null" : "N/A")}");
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
    // SKIP TURN — record a zero-damage shot for the active slot, advance turn
    // =========================================================================

    /// <summary>
    /// Host-only: skip the active player's turn by recording a zero-damage shot
    /// and driving the normal post-shot flow. If more players remain, swaps and
    /// draws the next ball. If everyone has shot, forces BattleController into
    /// PRE_ATTACK_SPAWN_CHECK so the attack phase proceeds.
    /// </summary>
    public void SkipCurrentTurn(int requestingSlot, string source)
    {
        if (!_mode.IsHosting) return;
        if (_coopStateManager.TotalPlayerCount < 2) return;

        int activeSlot = _coopStateManager.ActivePlayerSlot;
        if (requestingSlot != activeSlot)
        {
            _log.LogWarning($"[CoopSubs] SkipCurrentTurn denied — requester {requestingSlot} is not active slot {activeSlot} (source={source})");
            return;
        }
        if (_turnManager.Phase != TurnPhase.PLAYER_AIMING)
        {
            _log.LogWarning($"[CoopSubs] SkipCurrentTurn denied — Phase is {_turnManager.Phase}, not PLAYER_AIMING (source={source})");
            return;
        }

        _log.LogInfo($"[CoopSubs] SkipCurrentTurn source={source}, slot={activeSlot}");

        // Zero out peg tallies so the accumulated shot records as zero damage.
        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
        if (bc != null)
        {
            try
            {
                AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_criticalHitCount")?.SetValue(null, 0);
                AccessTools.Field(typeof(BattleController), "_numPegsHit")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_cactusDamageTally")?.SetValue(bc, 0);
                AccessTools.Field(typeof(BattleController), "_damageMultiplier")?.SetValue(bc, 1f);
                AccessTools.Field(typeof(BattleController), "_damageBonus")?.SetValue(bc, 0);
            }
            catch (Exception ex) { _log.LogWarning($"[CoopSubs] Skip: tally reset failed: {ex.Message}"); }

            // Destroy the aim ball and zero the remaining counter so physics
            // doesn't fire a free shot once we return control.
            try
            {
                var activeBallField = AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                if (activeBallField?.GetValue(bc) is GameObject activeBall)
                {
                    UnityEngine.Object.Destroy(activeBall);
                    activeBallField.SetValue(bc, null);
                }
                AccessTools.Field(typeof(BattleController), "_remainingPachinkoBalls")?.SetValue(bc, 0);
            }
            catch (Exception ex) { _log.LogWarning($"[CoopSubs] Skip: clear ball failed: {ex.Message}"); }
        }

        // Run the normal post-shot flow. This records the zero-damage shot,
        // marks MarkShotFired, saves state, advances turn, and for PLAYER_AIMING
        // (more players left) swaps + DrawBalls the next player.
        OnShotComplete();

        // If no one else is left, push BattleController into the attack phase
        // since there's no natural AWAITING_SHOT_COMPLETION transition to ride on
        // (we suppressed ball physics).
        if (_turnManager.Phase == TurnPhase.ALL_DONE)
        {
            try
            {
                BattleController.CurrentBattleState = BattleController.BattleState.PRE_ATTACK_SPAWN_CHECK;
                _log.LogInfo("[CoopSubs] Skip: forced BattleState -> PRE_ATTACK_SPAWN_CHECK");
            }
            catch (Exception ex) { _log.LogWarning($"[CoopSubs] Skip: state transition failed: {ex.Message}"); }
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

            // Revive dead players with 1 HP so they can participate in rewards
            // and continue to the next battle
            foreach (var kvp in _coopStateManager.PlayerStates)
            {
                var state = kvp.Value;
                if (state.IsInitialized && state.CurrentHealth <= 0)
                {
                    state.CurrentHealth = 1;
                    _log.LogInfo($"[CoopSubs] OnVictory: revived dead player slot {kvp.Key} '{state.PlayerName}' with 1 HP");
                }
            }

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
        public List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> StatusEffectsToApply;
        public int NumPegsHit;
        public int CriticalHitCount;
        public string OrbPrefabName;
        // Pincer Maneuver: damage above is the halved primary shot; the coop
        // sequencer must apply the same damage to the farthest enemy.
        public bool HasReverseShot;
    }

    /// <summary>
    /// Returns ALL players' accumulated shot data for per-player damage resolution.
    /// Called by the DoAttack Harmony prefix. Clears the data after consumption.
    /// Host damage is included because the RestoreAttackFromPrefab → ShotBehavior
    /// physics pipeline is unreliable (shots can miss due to collision/raycast issues).
    /// Applying all damage directly via Enemy.Damage() is more robust.
    /// </summary>
    internal static List<PlayerAttackData> ConsumeNonHostShotData()
    {
        var inst = Instance;
        if (inst == null) return null;

        var result = new List<PlayerAttackData>();
        foreach (var kvp in inst._accumulatedShotData)
        {
            var d = kvp.Value;
            result.Add(new PlayerAttackData
            {
                SlotIndex = kvp.Key,
                PlayerName = d.PlayerName,
                Damage = d.PrecomputedDamage,
                TargetEnemyGuid = d.TargetEnemyGuid,
                IsAoE = d.IsAoE,
                IsHeal = d.IsHeal,
                StatusEffectsToApply = d.StatusEffectsToApply,
                NumPegsHit = d.NumPegsHit,
                CriticalHitCount = d.CriticalHitCount,
                OrbPrefabName = d.OrbPrefabName,
                HasReverseShot = d.HasReverseShot,
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

    /// <summary>Temporary orb instance created at ALL_DONE; destroyed after DoAttack.</summary>
    internal static GameObject TempOrbInstance;

    /// <summary>
    /// Instantiate the orb prefab, initialize its Attack component, and set it as
    /// AttackManager._attack so DoAttack uses the correct damage formula.
    /// </summary>
    private static void RestoreAttackFromPrefab(BattleController bc, GameObject orbPrefab, string label)
    {
        // Clean up any previous temp orb
        if (TempOrbInstance != null)
        {
            UnityEngine.Object.Destroy(TempOrbInstance);
            TempOrbInstance = null;
        }

        // Instantiate offscreen so it's invisible but active (Fire() needs active components)
        TempOrbInstance = UnityEngine.Object.Instantiate(orbPrefab, new Vector3(-999, -999, 0), UnityEngine.Quaternion.identity);
        TempOrbInstance.name = $"CoopTempOrb_{label}";

        var attack = TempOrbInstance.GetComponent<Battle.Attacks.Attack>();
        if (attack == null)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopSubs] Prefab '{orbPrefab.name}' has no Attack component!");
            UnityEngine.Object.Destroy(TempOrbInstance);
            TempOrbInstance = null;
            return;
        }

        // Initialize the Attack with the current singletons (host's state is loaded at this point)
        var amField = AccessTools.Field(typeof(BattleController), "_attackManager");
        var am = amField?.GetValue(bc) as Battle.Attacks.AttackManager;
        var dmField = AccessTools.Field(typeof(BattleController), "_deckManager");
        var dm = dmField?.GetValue(bc) as DeckManager;
        var rmField = AccessTools.Field(typeof(BattleController), "_relicManager");
        var rm = rmField?.GetValue(bc) as Relics.RelicManager;
        var cmField = AccessTools.Field(typeof(BattleController), "_cruciballManager");
        var cm = cmField?.GetValue(bc) as Cruciball.CruciballManager;
        var phcField = AccessTools.Field(typeof(BattleController), "_playerHealthController");
        var phc = phcField?.GetValue(bc) as PlayerHealthController;
        var psecField = AccessTools.Field(typeof(BattleController), "_playerStatusEffectController");
        var psec = psecField?.GetValue(bc) as Battle.StatusEffects.PlayerStatusEffectController;

        var playerField = AccessTools.Field(typeof(BattleController), "_playerTransform");
        var playerTransform = playerField?.GetValue(bc) as UnityEngine.Transform;
        Vector2 playerPos = playerTransform != null ? (Vector2)playerTransform.position : Vector2.zero;

        attack.Initialize(playerPos, am, rm, dm, cm, phc, psec);

        // Clone the instanceID from the matching deck entry so that
        // IsAttackUnique can correctly identify this orb in the deck
        // (needed for UNIQUE_ORBS_BUFF / Spinventoriginality relic).
        // Initialize called SoftInit(forUI:false) which set applyUniqueBuff
        // with the wrong instanceID; we need to re-check after cloning.
        if (dm?.battleDeck != null)
        {
            foreach (var deckOrb in dm.battleDeck)
            {
                if (deckOrb != null)
                {
                    var deckAttack = deckOrb.GetComponent<Battle.Attacks.Attack>();
                    if (deckAttack != null && deckAttack.locNameString == attack.locNameString)
                    {
                        attack.CloneInstanceId(deckAttack);
                        attack.CheckUniqueBuff("");
                        break;
                    }
                }
            }
        }

        // Compute relic-based damage buffs (UNIQUE_ORBS_BUFF, INCREASE_STRENGTH_SMALL, etc.)
        // This mirrors what BattleController.DrawBall does after InitializeAttack.
        attack.CalculateStaticDamageBuffs(saveResults: true);

        // Set as the active attack in AttackManager
        if (am != null)
        {
            var atkField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
            atkField?.SetValue(am, attack);
            am.isHeal = attack is HealAction;
        }

        MultiplayerPlugin.Logger?.LogInfo($"[CoopSubs] Restored {label} Attack from prefab: {orbPrefab.name}");
    }

    /// <summary>
    /// Clean up the temporary orb instance after DoAttack finishes.
    /// Called from the DoAttack postfix in MultiplayerClientPatches.
    /// </summary>
    internal static void CleanupTempOrb()
    {
        if (TempOrbInstance != null)
        {
            UnityEngine.Object.Destroy(TempOrbInstance);
            TempOrbInstance = null;
        }
    }

    /// <summary>
    /// Capture status effects from an orb at shot completion time for non-host players,
    /// since their effects won't be applied by the normal DoAttack pipeline (which only
    /// processes the host's Attack object).
    ///
    /// We collect from three sources matching the native per-hit pipeline:
    ///   1. attack.GetStatusEffects(critCount) — covers the Attack base class's relic
    ///      effects (ATTACKS_DEAL_BLIND, Transpherency, Poison, Exploitaball) AND the
    ///      subclass overrides that orbs like BlindOrb, ThornOrb, PoisonOrb rely on
    ///      (e.g. BlindAttack adds Blind(BlindIntensity=11)). Invoked natively per-hit
    ///      from ProjectileAttack.DoDamage; we invoke it once per shot for replay.
    ///   2. AddStatusEffectOnHit components — per-hit IAffectEnemyOnHit effects
    ///      attached to the orb prefab.
    ///   3. AddRandomStatusEffectOnHit — Roundreloquence's random roll.
    /// </summary>
    private static List<(Battle.StatusEffects.StatusEffectType, int)> CaptureOrbStatusEffects(
        Battle.Attacks.Attack attack, int critCount)
    {
        var effects = new List<(Battle.StatusEffects.StatusEffectType, int)>();
        try
        {
            // 1. attack.GetStatusEffects — relic-driven base effects + Attack subclass overrides
            try
            {
                var fromAttack = attack.GetStatusEffects(critCount);
                if (fromAttack != null)
                {
                    foreach (var se in fromAttack)
                    {
                        if (se == null || se.EffectType == Battle.StatusEffects.StatusEffectType.None)
                            continue;
                        effects.Add((se.EffectType, se.Intensity));
                    }
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopSubs] attack.GetStatusEffects failed: {ex.Message}");
            }

            // 2. AddStatusEffectOnHit — orb-prefab per-hit effects
            foreach (var seh in attack.GetComponents<Battle.Attacks.AttackBehaviours.AddStatusEffectOnHit>())
            {
                int intensity = seh.intensity;
                if (seh.isCritThresholdCumulative && seh.critCountThreshold > 0)
                    intensity += critCount / seh.critCountThreshold * seh.critAddIntensity;
                else if (seh.critCountThreshold <= critCount)
                    intensity += seh.critAddIntensity;
                effects.Add((seh.type, intensity));
            }

            // 3. AddRandomStatusEffectOnHit — Roundreloquence orb
            foreach (var _ in attack.GetComponents<Battle.Attacks.AttackBehaviours.AddRandomStatusEffectOnHit>())
            {
                int idx = UnityEngine.Random.Range(0,
                    Battle.Attacks.AttackBehaviours.AddRandomStatusEffectOnHit.RoundreloquencePotentialEffects.Length);
                effects.Add((
                    Battle.Attacks.AttackBehaviours.AddRandomStatusEffectOnHit.RoundreloquencePotentialEffects[idx],
                    Battle.Attacks.AttackBehaviours.AddRandomStatusEffectOnHit.RoundreloquenceEffectIntensity[idx]));
            }

            if (effects.Count > 0)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[CoopSubs] Captured {effects.Count} status effect(s) from orb " +
                    $"({attack.GetType().Name}): " +
                    string.Join(", ", effects.Select(e => $"{e.Item1}({e.Item2})")));
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopSubs] CaptureOrbStatusEffects failed: {ex.Message}");
        }
        return effects.Count > 0 ? effects : null;
    }

    /// <summary>
    /// Apply self-granting post-attack status effects (Ballusion from Evasive Maneuvorb,
    /// Muscircle, Ballwark from shield pegs, etc.) to the currently loaded player's
    /// PlayerStatusEffectController. This replicates the game's native
    /// CallPostAttackOperations logic, which is skipped in coop because the DoAttack
    /// Harmony prefix returns false.
    ///
    /// Invokes the component's own HandleAttackFinished method directly so that the
    /// crit/shield-peg intensity scaling matches native behavior exactly.
    /// </summary>
    private static void ApplySelfPostAttackBuffs(Battle.Attacks.Attack attack, int critCount, int slotIndex)
    {
        try
        {
            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl == null) return;

            int grantCount = 0;
            int grantShieldCount = 0;

            foreach (var grant in attack.GetComponents<Battle.Attacks.AttackBehaviours.GrantStatusAfterAttack>())
            {
                try
                {
                    grant.HandleAttackFinished(statusCtrl);
                    grantCount++;
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[CoopSubs] GrantStatusAfterAttack.HandleAttackFinished failed: {ex.Message}");
                }
            }

            foreach (var grant in attack.GetComponents<Battle.Attacks.AttackBehaviours.GrantStatusAfterAttackPerShieldPeg>())
            {
                try
                {
                    grant.HandleAttackFinished(statusCtrl);
                    grantShieldCount++;
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[CoopSubs] GrantStatusAfterAttackPerShieldPeg.HandleAttackFinished failed: {ex.Message}");
                }
            }

            if (grantCount > 0 || grantShieldCount > 0)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[CoopSubs] Applied self-buffs for slot {slotIndex}: " +
                    $"GrantStatusAfterAttack={grantCount}, GrantStatusAfterAttackPerShieldPeg={grantShieldCount}, crits={critCount}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopSubs] ApplySelfPostAttackBuffs failed: {ex.Message}");
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

    // =========================================================================
    // PER-PLAYER DEFENSIVE BUFF RESOLUTION
    // =========================================================================

    /// <summary>
    /// Apply a player's defensive status effects (Ballusion, Intangiball, Ballwark)
    /// to incoming damage. Modifies the player's saved status effects in-place
    /// (e.g. consuming Ballwark stacks, reducing Ballusion on dodge).
    /// Returns the effective damage after all defensive buffs.
    /// </summary>
    private float ApplyDefensiveBuffs(GameState.CoopPlayerState state, float rawDamage)
    {
        float damage = rawDamage;

        int ballusionStacks = GetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballusion);
        int intangiballCap = GetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Intangiball);
        int ballwarkStacks = GetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballwark);

        // 1. Ballusion (evasion): 1% dodge chance per stack
        if (ballusionStacks > 0 && damage > 0f)
        {
            float evasionChance = ballusionStacks * Battle.StatusEffects.StatusEffectData.BALLUSION_EVASION_PER_STACK;
            float roll = UnityEngine.Random.value;
            if (roll < evasionChance)
            {
                // Dodge! Reduce Ballusion stacks by ~50%
                float reduction = Battle.StatusEffects.StatusEffectData.BALLUSION_REDUCTION_PERCENTAGE;

                // Check for relics that modify reduction
                if (HasRelic(state, Relics.RelicEffect.BALLUSION_DOUBLE_MAX) ||
                    HasRelic(state, Relics.RelicEffect.RETAIN_DODGE_BETWEEN_BATTLES))
                    reduction -= 0.1f;
                if (HasRelic(state, Relics.RelicEffect.BALLUSION_DOUBLE_GAIN_AND_LOSS))
                    reduction *= 2f;

                float keep = Mathf.Clamp(1f - reduction, 0f, 1f);
                int newStacks = Mathf.RoundToInt(ballusionStacks * keep);
                SetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballusion, newStacks);

                _log.LogInfo($"[CoopSubs] Slot {state.SlotIndex} DODGED (Ballusion {ballusionStacks}->{newStacks}, roll={roll:F3} < {evasionChance:F3})");
                damage = 0f;
            }
        }

        // 2. Intangiball (damage cap)
        if (intangiballCap > 0 && damage > 0f)
        {
            damage = Mathf.Min(damage, intangiballCap);
        }

        // 3. Ballwark (armor absorption)
        if (ballwarkStacks > 0 && damage > 0f)
        {
            float armour = ballwarkStacks;
            if (armour >= damage)
            {
                armour -= damage;
                damage = 0f;
            }
            else
            {
                damage -= armour;
                armour = 0f;
            }
            SetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballwark, Mathf.RoundToInt(armour));
            _log.LogInfo($"[CoopSubs] Slot {state.SlotIndex} Ballwark absorbed: {ballwarkStacks}->{Mathf.RoundToInt(armour)}");
        }

        return Mathf.Max(0f, damage);
    }

    private static int GetEffectIntensity(GameState.CoopPlayerState state, int effectType)
    {
        foreach (var e in state.StatusEffects)
            if (e.EffectType == effectType) return e.Intensity;
        return 0;
    }

    private static void SetEffectIntensity(GameState.CoopPlayerState state, int effectType, int intensity)
    {
        for (int i = 0; i < state.StatusEffects.Count; i++)
        {
            if (state.StatusEffects[i].EffectType == effectType)
            {
                if (intensity <= 0)
                    state.StatusEffects.RemoveAt(i);
                else
                    state.StatusEffects[i].Intensity = intensity;
                return;
            }
        }
        // Effect not in list yet — add if positive
        if (intensity > 0)
        {
            state.StatusEffects.Add(new GameState.SerializedStatusEffect
            {
                EffectType = effectType,
                Intensity = intensity,
            });
        }
    }

    private static bool HasRelic(GameState.CoopPlayerState state, Relics.RelicEffect effect)
    {
        int effectInt = (int)effect;
        foreach (var r in state.OwnedRelics)
            if (r.Effect == effectInt) return true;
        return false;
    }
}
