using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using BepInEx.Logging;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Events.Subscriptions.Coop;
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
///
/// The heavy lifting is delegated to focused interfaces:
///   • IBattleControllerUpdateManager — all reflection on BattleController fields/methods
///   • ICoopDeckManager              — battle-deck rebuild + non-host starting bonuses
///   • IOrbStatusEffectApplier       — orb status capture + temp-orb attack restoration
///   • IBuffApplier                  — defensive buff resolution against stored state
/// </summary>
public sealed class CoopSubscriptions
{
    private readonly IMultiplayerMode _mode;
    private readonly CoopStateManager _coopStateManager;
    private readonly TurnManager _turnManager;
    private readonly IGameStateSyncService _syncService;
    private readonly ManualLogSource _log;

    private readonly IBattleControllerUpdateManager _bcUpdater;
    private readonly ICoopDeckManager _deckMgr;
    private readonly IOrbStatusEffectApplier _orbApplier;
    private readonly IBuffApplier _buffApplier;

    private readonly Dictionary<int, ShotDamageData> _accumulatedShotData = new Dictionary<int, ShotDamageData>();

    /// <summary>
    /// Stores the target GUID from the last pending shot (client → host).
    /// Set by ExecutePendingShot before consuming the pending shot.
    /// Read by OnShotComplete to record per-player targeting.
    /// </summary>
    internal static string LastPendingShotTargetGuid { get; set; }

    /// <summary>
    /// High-water mark for the running pegTally observed during the current shot.
    /// Updated from BattleControllerPatches.HandlePegActivated_Postfix on every
    /// peg hit, then read in OnShotComplete and used as a defensive fallback if
    /// BC's _pegMultiplierDamageTally has been zeroed by the time we capture
    /// (e.g. multiball satellites destroying themselves in unusual orders, or
    /// any future native-game flow that resets the tally before our hook fires).
    /// Reset on every new shot via ShotFired (CoopSubscriptions also clears it
    /// in OnAwaitingShot and OnShotComplete after consuming).
    /// </summary>
    internal static int HighWaterPegTally { get; set; }

    /// <summary>
    /// High-water mark for am.GetCurrentDamage observed during the current shot.
    /// Mirrors HighWaterPegTally but stores the fully-computed damage so it
    /// captures any per-peg crit/multiplier/bonus updates that happened before
    /// the satellite's destruction. Used as a sanity floor in OnShotComplete.
    /// </summary>
    internal static long HighWaterDamage { get; set; }

    /// <summary>
    /// Number of multiball-spawn events observed during the current shot.
    /// Updated by PachinkoBallPatches when OnAdditionalPachinkoBallCreated fires.
    /// Logged in OnShotComplete to make multiball-specific issues diagnosable.
    /// </summary>
    internal static int MultiballSpawnCount { get; set; }

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
    /// Idempotency guard for OnVictory. BattleController.OnVictory can fire
    /// repeatedly after a boss kill (the BossHeal cascade and the post-battle
    /// canvas re-enable can re-trigger CompleteVictory), which would otherwise
    /// re-broadcast PostBattleStartEvent every ~250ms — causing the host to
    /// stall on "Your Turn! Aim and shoot" and clients to be reset into
    /// post-battle screens repeatedly. Cleared in OnBattleStarted so the next
    /// battle's victory works normally.
    /// </summary>
    private bool _victoryHandledThisBattle;

    /// <summary>
    /// Singleton instance so the BattleController.Awake Postfix can re-subscribe.
    /// The game's static delegates may lose subscribers across scene loads,
    /// so we re-subscribe at the start of every battle.
    /// </summary>
    internal static CoopSubscriptions Instance { get; private set; }

    public CoopSubscriptions(
        IMultiplayerMode mode,
        CoopStateManager coopStateManager,
        TurnManager turnManager,
        IGameStateSyncService syncService,
        IBattleControllerUpdateManager bcUpdater,
        ICoopDeckManager deckMgr,
        IOrbStatusEffectApplier orbApplier,
        IBuffApplier buffApplier,
        ManualLogSource log)
    {
        _mode = mode;
        _coopStateManager = coopStateManager;
        _turnManager = turnManager;
        _syncService = syncService;
        _bcUpdater = bcUpdater;
        _deckMgr = deckMgr;
        _orbApplier = orbApplier;
        _buffApplier = buffApplier;
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

    // =========================================================================
    // ENEMY ATTACK PHASE — capture & distribute damage to non-active players
    // =========================================================================

    /// <summary>
    /// Called when the enemy attack phase begins. Snapshot the active player's
    /// current health so we can compute how much damage was dealt after it resolves.
    /// </summary>
    private void OnAttackStarted()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

        if (rawDamage <= 0f)
        {
            return;
        }

        var activeSlot = _coopStateManager.ActivePlayerSlot;
        foreach (var state in _coopStateManager.PlayerStates.Values)
        {
            if (state.SlotIndex == activeSlot)
            {
                continue;
            }

            var oldHp = state.CurrentHealth;
            var effectiveDamage = _buffApplier.ApplyDefensiveBuffs(state, rawDamage);
            state.CurrentHealth = Mathf.Max(0f, state.CurrentHealth - effectiveDamage);
            var actualTaken = oldHp - state.CurrentHealth;
            if (actualTaken > 0)
            {
                state.DamageTaken += (long)actualTaken;
            }

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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

        if (amount <= 0f)
        {
            return;
        }

        DistributeDamageToNonActive(amount, "OnPlayerDamaged");
        _damageDistributedSinceAttackStart += amount;

        // Also mirror the damage into the active player's CoopPlayerState so
        // TurnManager.StartNewRound can correctly auto-skip them if they just
        // died. Without this, state.CurrentHealth only refreshes on the next
        // SaveActivePlayerState (swap boundary) — which means a player who
        // dies mid-damage-phase still gets scheduled as round-start slot 0.
        var activeSlot = _coopStateManager.ActivePlayerSlot;
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
                if (amount > 0)
                {
                    activeState.DamageTaken += (long)amount;
                }
            }
        }
    }

    /// <summary>
    /// Called from HealthSubscriptions.OnPlayerHealed on every phc.Heal() call.
    /// Mirrors the heal into the active player's CoopPlayerState so heals
    /// (e.g. Doctorb, lifesteal) that fire outside the SaveActivePlayerState
    /// boundary are not lost when the next swap snapshots singletons.
    /// </summary>
    public void HandleImmediateHeal(float amount)
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        if (amount <= 0f)
        {
            return;
        }

        var activeSlot = _coopStateManager.ActivePlayerSlot;
        if (activeSlot < 0)
        {
            return;
        }

        var activeState = _coopStateManager.GetPlayerState(activeSlot);
        if (activeState == null)
        {
            return;
        }

        var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        if (phc != null)
        {
            activeState.CurrentHealth = Mathf.Min(activeState.MaxHealth, phc.CurrentHealth);
        }
    }

    /// <summary>
    /// Apply a HealAction (Doctorb-style) heal to the shooter at shot-complete
    /// time. In coop, HealAction.Fire never runs because PlayCoopAttackSequence
    /// replaces DoAttack — so the heal would otherwise be deferred to
    /// end-of-round playback (or lost if the round never completes). This
    /// updates both the live PlayerHealthController (for the active shooter)
    /// and the per-slot CoopPlayerState, then dispatches PlayerHealedEvent so
    /// the client visuals refresh on the next frame instead of after a heartbeat.
    /// </summary>
    private void ApplyImmediateHeal(int shooterSlot, long healAmount, string orbPrefabName, string targetGuid)
    {
        var state = _coopStateManager.GetPlayerState(shooterSlot);
        if (state == null)
        {
            return;
        }

        var before = state.CurrentHealth;
        var newHealth = Mathf.Min(state.MaxHealth, before + healAmount);
        var actualHeal = newHealth - before;
        if (actualHeal <= 0f)
        {
            _log.LogInfo($"[CoopSubs] Heal slot {shooterSlot}: already at full hp ({before}/{state.MaxHealth}), no heal applied");
            return;
        }

        state.CurrentHealth = newHealth;

        // Push into live PHC if the shooter is still the active slot — at
        // shot-complete time it is, so SaveActivePlayerState (called right
        // after OnShotComplete returns) reads the healed value.
        if (_coopStateManager.ActivePlayerSlot == shooterSlot)
        {
            try
            {
                var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
                if (phc != null)
                {
                    var healthField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
                    var healthVar = healthField?.GetValue(phc) as FloatVariable;
                    healthVar?.Set(newHealth);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[CoopSubs] Heal: failed to push hp to PHC: {ex.Message}");
            }
        }

        try
        {
            var registry = MultiplayerPlugin.Services?.TryResolve<IGameEventRegistry>(out var reg) == true ? reg : null;
            registry?.Dispatch(new Events.Network.Health.PlayerHealedEvent
            {
                Amount = actualHeal,
                RemainingHealth = newHealth,
                TargetSlotIndex = shooterSlot,
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] Heal dispatch failed: {ex.Message}");
        }

        // Apply HealAction's secondary damage (damageTargetedEnemyMultiplier /
        // damageAllEnemiesMultiplier) directly so we don't need to defer to
        // PlayCoopAttackSequence. Mirrors HealAction.DoHealAction.
        try
        {
            var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orbPrefabName);
            var heal = orbPrefab?.GetComponent<HealAction>();
            if (heal == null)
            {
                _log.LogInfo($"[CoopSubs] Heal slot {shooterSlot}: +{actualHeal} hp -> {newHealth}/{state.MaxHealth}");
                return;
            }

            var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (em == null)
            {
                return;
            }

            if (heal.damageTargetedEnemyMultiplier > 0f)
            {
                var dmg = Mathf.RoundToInt(actualHeal * heal.damageTargetedEnemyMultiplier);
                if (dmg > 0)
                {
                    Battle.Enemies.Enemy primary = null;
                    var services = MultiplayerPlugin.Services;
                    if (!string.IsNullOrEmpty(targetGuid)
                        && services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true)
                    {
                        primary = eid.Find(targetGuid);
                    }

                    if (primary == null || primary.CurrentHealth <= 0f)
                    {
                        primary = em.GetFarthestEnemyFromPlayer();
                    }

                    if (primary != null && primary.CurrentHealth > 0f)
                    {
                        EnemySubscriptions.DamageAttributionSlotOverride = shooterSlot;
                        try
                        {
                            primary.Damage(
                                dmg,
                                screenshake: false,
                                0.25f,
                                1f,
                                unblockable: false,
                                Battle.Enemies.Enemy.EnemyDamageSource.TargetedAttack);
                        }
                        finally
                        {
                            EnemySubscriptions.DamageAttributionSlotOverride = -1;
                        }
                    }
                }
            }
            else if (heal.damageAllEnemiesMultiplier > 0f)
            {
                var dmg = Mathf.RoundToInt(actualHeal * heal.damageAllEnemiesMultiplier);
                if (dmg > 0)
                {
                    EnemySubscriptions.DamageAttributionSlotOverride = shooterSlot;
                    try
                    {
                        foreach (var e in em.Enemies)
                        {
                            if (e != null && e.CurrentHealth > 0f)
                            {
                                e.Damage(
                                    dmg,
                                    screenshake: false,
                                    0.25f,
                                    1f,
                                    unblockable: false,
                                    Battle.Enemies.Enemy.EnemyDamageSource.AOE);
                            }
                        }
                    }
                    finally
                    {
                        EnemySubscriptions.DamageAttributionSlotOverride = -1;
                    }
                }
            }

            _log.LogInfo($"[CoopSubs] Heal slot {shooterSlot}: +{actualHeal} hp -> {newHealth}/{state.MaxHealth} " +
                $"(orb={orbPrefabName}, targetMult={heal.damageTargetedEnemyMultiplier}, allMult={heal.damageAllEnemiesMultiplier})");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] Heal secondary damage failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called after the enemy attack phase completes. Compute the damage dealt
    /// to the active player and apply it to all other players' stored state.
    /// Then check if any player is dead.
    /// </summary>
    private void OnTurnComplete()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

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
                var currentHealth = phc.CurrentHealth;
                var damage = _healthBeforeAttack - currentHealth - _damageDistributedSinceAttackStart;

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
                var nextSlot = 0;
                foreach (var slot in _turnManager.TurnOrder)
                {
                    var st = _coopStateManager.GetPlayerState(slot);
                    if (st != null && st.CurrentHealth > 0)
                    {
                        nextSlot = slot;
                        break;
                    }
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
            _orbApplier.CleanupTempOrb();
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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

        _accumulatedShotData.Clear();
        Multipeglin.UI.PendingDamageOverlay.ClearAll();
        _victoryHandledThisBattle = false;
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
                if (kvp.Key == 0)
                {
                    continue; // Always skip host (slot 0)
                }

                var state = kvp.Value;
                if (state.CompleteDeck.Count > 0)
                {
                    // Clear stale battle state so LoadDeckState + EnsureBattleDeckPopulated
                    // rebuilds everything fresh from CompleteDeck
                    state.BattleDeck.Clear();
                    state.ShuffledOrder?.Clear();
                    _log.LogInfo($"[CoopSubs] Battle init: slot {kvp.Key} — rebuilding deck from completeDeck ({state.CompleteDeck.Count})");
                    _coopStateManager.SwapToPlayer(kvp.Key);
                    _deckMgr.EnsureBattleDeckPopulated($"battle init slot {kvp.Key}");
                    _deckMgr.ApplyNonHostStartingBonuses(kvp.Key);
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
            catch (Exception syncEx)
            {
                _log.LogWarning($"[CoopSubs] Battle init SyncAll failed: {syncEx.Message}");
            }
        }
        catch (Exception ex)
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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

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
            var bc = _bcUpdater.GetBattleController();
            if (bc != null)
            {
                _bcUpdater.ResetShotTallies(bc);
            }

            // Send clear event to client so its overlay resets too
            try
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
                {
                    reg.Dispatch(new PendingDamagePreviewEvent());
                }
            }
            catch
            {
            }

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
        var bc = _bcUpdater.GetBattleController();

        try
        {
            if (bc != null)
            {
                _bcUpdater.DestroyActivePachinkoBall(bc);
                _bcUpdater.SetRemainingPachinkoBalls(bc, 0);
                _bcUpdater.ResetShotTallies(bc);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): pre-swap cleanup failed: {ex.Message}");
        }

        _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);

        // Silence DeckInfoManager callbacks during the non-host shuffle/draw so
        // they don't corrupt the host's deck tube display (same guard as
        // OnShotComplete). onPersistBallUsed MUST be silenced too: it fires
        // PersistBallDrawFinished → ShotDetailsWidget.UpdateDetailsWithoutAnim
        // → Attack.GetNameWithLevel on DeckInfoManager._nextOrb. After a swap,
        // _nextOrb still points at the previous slot's destroyed orb GameObject
        // and GetComponent throws — the throw aborts DrawBall mid-flight,
        // leaving _activePachinkoBall null and softlocking the next shot.
        DeckManager.BallDrawn savedOnBallUsed = null;
        DeckManager.Shuffled savedOnDeckShuffled = null;
        DeckManager.PersistBallUsed savedOnPersistBallUsed = null;
        try
        {
            savedOnBallUsed = DeckManager.onBallUsed;
            savedOnDeckShuffled = DeckManager.onDeckShuffled;
            savedOnPersistBallUsed = DeckManager.onPersistBallUsed;
            DeckManager.onBallUsed = _ => { };
            DeckManager.onDeckShuffled = _ => { };
            DeckManager.onPersistBallUsed = () => { };
        }
        catch (Exception cbEx)
        {
            _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): callback disconnect failed: {cbEx.Message}");
        }

        if (!_deckMgr.EnsureBattleDeckPopulated($"{context} swap"))
        {
            _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): EnsureBattleDeckPopulated failed — deck may be empty");
        }

        // Rebuild the deck tube display for the newly loaded slot BEFORE the
        // manual DrawBall. Otherwise _displayOrbs still holds stale orb instances
        // from the previous swap (created by the prior RebuildDeckInfoDisplay +
        // native DrawBall), and the host's deck UI shows those stale orbs piled
        // on top of each other while the active shooter is a different slot.
        _coopStateManager.RebuildDeckInfoDisplay();

        try
        {
            _bcUpdater.SetBattleState(BattleController.BattleState.AWAITING_SHOT);
            if (bc != null)
            {
                _bcUpdater.InvokeDrawBall(bc);
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
                if (savedOnBallUsed != null)
                {
                    DeckManager.onBallUsed = savedOnBallUsed;
                }

                if (savedOnDeckShuffled != null)
                {
                    DeckManager.onDeckShuffled = savedOnDeckShuffled;
                }

                if (savedOnPersistBallUsed != null)
                {
                    DeckManager.onPersistBallUsed = savedOnPersistBallUsed;
                }
            }
            catch (Exception restoreEx)
            {
                _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): callback restore failed: {restoreEx.Message}");
            }
        }

        try
        { _syncService.SyncAll($"{context}"); }
        catch (Exception syncEx) { _log.LogWarning($"[CoopSubs] SwapAndRedraw ({context}): SyncAll failed: {syncEx.Message}"); }
    }

    /// <summary>
    /// If the active turn slot has 0 HP while Phase==PLAYER_AIMING, run
    /// SkipCurrentTurn. Repeats until a live player is current or the round ends.
    /// </summary>
    private void SkipActiveSlotIfDead(string reason)
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

        var safety = 0;
        while (_turnManager.Phase == TurnPhase.PLAYER_AIMING && safety++ < 16)
        {
            var slot = _turnManager.CurrentPlayerSlot;
            if (slot < 0)
            {
                break;
            }

            var state = _coopStateManager.GetPlayerState(slot);
            if (state == null || !state.IsInitialized)
            {
                break;
            }

            if (state.CurrentHealth > 0)
            {
                break;
            }

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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

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

        var activeSlot = _coopStateManager.ActivePlayerSlot;

        // Read BattleController's peg damage tallies for this player's shot
        var bc = _bcUpdater.GetBattleController();

        if (bc != null)
        {
            var pegTally = _bcUpdater.GetPegMultiplierDamageTally(bc);
            var critCount = _bcUpdater.GetCriticalHitCount();
            var numPegs = _bcUpdater.GetNumPegsHit(bc);
            var cactusTally = _bcUpdater.GetCactusDamageTally(bc);
            var dmgMult = _bcUpdater.GetDamageMultiplier(bc);
            var dmgBonus = _bcUpdater.GetDamageBonus(bc);

            // Pre-compute this player's damage using the currently loaded Attack
            var am = _bcUpdater.GetAttackManager(bc);
            long precomputedDamage = 0;
            var isAoE = false;
            var isHeal = false;
            List<(Battle.StatusEffects.StatusEffectType, int)> capturedEffects = null;
            string capturedOrbName = null;

            // Pincer Maneuver (ADDITIONAL_REVERSE_PROJECTILE_ATTACK): when the active
            // player has this relic, ProjectileAttack.Fire halves the damage (rounded
            // up) and schedules a second shot at the farthest enemy with the same
            // halved value. We bypass Fire in coop, so replicate the split here.
            var hasReverseShot = false;
            var hasTargetedSplash = false;
            var hasTargetedHitAll = false;
            {
                var rms = Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                var rm = rms != null && rms.Length > 0 ? rms[0] : null;
                if (rm != null)
                {
                    hasReverseShot = rm.RelicEffectActive(Relics.RelicEffect.ADDITIONAL_REVERSE_PROJECTILE_ATTACK);
                    hasTargetedSplash = rm.RelicEffectActive(Relics.RelicEffect.SPLASH_EFFECT_ON_TARGETED_ATTACKS);
                    hasTargetedHitAll = rm.RelicEffectActive(Relics.RelicEffect.TARGETED_ATTACKS_HIT_ALL);
                }
            }

            if (am != null)
            {
                precomputedDamage = am.GetCurrentDamage(pegTally, dmgMult, dmgBonus, critCount);

                // Defensive multiball fallback — if BC's pegTally has been
                // zeroed, or am._attack has been Unity-destroyed (which makes
                // GetCurrentDamage return 0), fall back to the high-water mark
                // observed during the shot. The HWM only goes UP per peg, so
                // the comparison is safe: if the natural capture is correct,
                // it will already equal or exceed the HWM.
                var hwmTally = HighWaterPegTally;
                var hwmDamage = HighWaterDamage;
                if (hwmDamage > precomputedDamage)
                {
                    _log.LogWarning(
                        $"[CoopSubs] Damage capture below HWM — using HWM. " +
                        $"captured={precomputedDamage} (pegTally={pegTally}), " +
                        $"hwm={hwmDamage} (pegTally={hwmTally}), " +
                        $"multiballSpawns={MultiballSpawnCount}, slot={activeSlot}");
                    precomputedDamage = hwmDamage;

                    // If the only reason capture failed is a zeroed pegTally,
                    // also recover the tally so downstream consumers (e.g. the
                    // ALL_DONE branch that writes back to BC for native attack)
                    // see consistent values.
                    if (pegTally == 0 && hwmTally > 0)
                    {
                        pegTally = hwmTally;
                    }
                }

                isHeal = am.isHeal;
                if (hasReverseShot && !isHeal && precomputedDamage > 0)
                {
                    var remainder = precomputedDamage % 2;
                    precomputedDamage = precomputedDamage / 2 + remainder;
                }

                // Check attack type to determine targeting behavior
                var attack = _bcUpdater.GetCurrentAttack(am);
                if (attack is Battle.Attacks.SimpleAttack)
                {
                    isAoE = true;
                }

                // Capture status effects from the orb's components for ALL slots (host included).
                // The DoAttack Harmony prefix skips the native attack pipeline in coop mode, so
                // GetStatusEffects is never invoked for the host either — without this capture,
                // PoisonOrb / BlindOrb / ThornOrb etc. would silently fail to debuff their target
                // even when fired by the host.
                if (attack != null)
                {
                    capturedEffects = _orbApplier.CaptureOrbStatusEffects(attack, critCount);
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
                    _orbApplier.ApplySelfPostAttackBuffs(attack, critCount, activeSlot);
                }

                // Capture the orb name for re-instantiation at ALL_DONE.
                // The active ball is still alive here (not destroyed until DrawBall for next player).
                // We store the prefab name so we can look it up via AssetLoading.GetOrbPrefab().
                try
                {
                    var activeBall = _bcUpdater.GetActivePachinkoBall(bc);
                    if (activeBall != null)
                    {
                        capturedOrbName = activeBall.name.Replace("(Clone)", string.Empty).Trim();
                    }
                    else
                    {
                        _log.LogWarning($"[CoopSubs] _activePachinkoBall was null at shot capture for slot {activeSlot}");
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[CoopSubs] Failed to capture orb name: {ex.Message}");
                }
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
                    {
                        targetGuid = eid.GetGuid(tmgr.currentTarget);
                    }
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
            {
                playerName = pState.PlayerName;
            }

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
                HasTargetedSplash = hasTargetedSplash,
                HasTargetedHitAll = hasTargetedHitAll,
            };

            _log.LogInfo($"[CoopSubs] Saved shot data for slot {activeSlot}: " +
                $"pegTally={pegTally}, crits={critCount}, pegsHit={numPegs}, " +
                $"damage={precomputedDamage}, target={targetGuid ?? "auto"}, isAoE={isAoE}, " +
                $"statusEffects={capturedEffects?.Count ?? 0}, orb={capturedOrbName ?? "NONE"}, " +
                $"multiballSpawns={MultiballSpawnCount}, hwmTally={HighWaterPegTally}, hwmDmg={HighWaterDamage}");

            // Doctorb-style heals (HealAction) never run their native Fire() in
            // coop because DoAttack is replaced by PlayCoopAttackSequence. Apply
            // the heal NOW so the shooter's hp updates this turn instead of
            // being deferred to end-of-round playback. Clear IsHeal afterward so
            // the replay path doesn't double-heal.
            if (isHeal && precomputedDamage > 0)
            {
                ApplyImmediateHeal(activeSlot, precomputedDamage, capturedOrbName, targetGuid);
                // Skip the replay path entirely — heal + secondary damage have
                // already landed. Leaving IsHeal=true would re-enter ApplyCoopHeal
                // (no-ops the heal, but could re-fire secondary damage); leaving
                // PrecomputedDamage non-zero would treat the shot as a normal
                // attack and damage an enemy for the heal amount.
                _accumulatedShotData[activeSlot].IsHeal = false;
                _accumulatedShotData[activeSlot].PrecomputedDamage = 0;
            }
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
                _bcUpdater.ResetShotTallies(bc);
            }

            _coopStateManager.SwapToPlayer(_turnManager.CurrentPlayerSlot);

            // Disconnect DeckInfoManager callbacks BEFORE EnsureBattleDeckPopulated.
            // If the non-host player's deck needs reshuffling,
            // ShuffleBattleDeck fires onDeckShuffled which would trigger the
            // host's DeckInfoManager animation and corrupt the host's deck
            // tube display. By disconnecting first, the reshuffle is silent.
            DeckManager.BallDrawn savedOnBallUsed = null;
            DeckManager.Shuffled savedOnDeckShuffled = null;
            DeckManager.PersistBallUsed savedOnPersistBallUsed = null;
            try
            {
                savedOnBallUsed = DeckManager.onBallUsed;
                savedOnDeckShuffled = DeckManager.onDeckShuffled;
                savedOnPersistBallUsed = DeckManager.onPersistBallUsed;
                DeckManager.onBallUsed = _ => { }; // no-op during non-host operations
                DeckManager.onDeckShuffled = _ => { }; // no-op during non-host shuffle
                DeckManager.onPersistBallUsed = () => { }; // PersistBallDrawFinished crashes on stale _nextOrb post-swap
            }
            catch (Exception cbEx)
            {
                _log.LogWarning($"[CoopSubs] Failed to disconnect DeckManager callbacks: {cbEx.Message}");
            }

            if (!_deckMgr.EnsureBattleDeckPopulated("shot complete swap"))
            {
                _log.LogWarning("[CoopSubs] EnsureBattleDeckPopulated failed after shot complete swap — deck may be empty");
            }

            // Override BattleState back to AWAITING_SHOT so the state machine
            // re-enters the aiming phase instead of proceeding to attack.
            _bcUpdater.SetBattleState(BattleController.BattleState.AWAITING_SHOT);

            // Manually trigger DrawBall since we bypassed the normal flow.
            try
            {
                if (bc != null)
                {
                    _bcUpdater.InvokeDrawBall(bc);
                    _log.LogInfo($"[CoopSubs] Manually called DrawBall for slot {_turnManager.CurrentPlayerSlot}");
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
                    if (savedOnBallUsed != null)
                    {
                        DeckManager.onBallUsed = savedOnBallUsed;
                    }

                    if (savedOnDeckShuffled != null)
                    {
                        DeckManager.onDeckShuffled = savedOnDeckShuffled;
                    }

                    if (savedOnPersistBallUsed != null)
                    {
                        DeckManager.onPersistBallUsed = savedOnPersistBallUsed;
                    }
                }
                catch (Exception restoreEx)
                {
                    _log.LogWarning($"[CoopSubs] Failed to restore DeckManager callbacks: {restoreEx.Message}");
                }
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
            try
            {
                _syncService.SyncAll("TurnSwap");
            }
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
                _bcUpdater.WriteShotTallies(
                    bc,
                    hostData.PegMultiplierDamageTally,
                    hostData.CriticalHitCount,
                    hostData.NumPegsHit,
                    hostData.CactusDamageTally,
                    hostData.DamageMultiplier,
                    hostData.DamageBonus);
                _log.LogInfo($"[CoopSubs] Host (slot 0) damage: pegTally={hostData.PegMultiplierDamageTally}, " +
                    $"crits={hostData.CriticalHitCount}, pegsHit={hostData.NumPegsHit}, " +
                    $"dmgMult={hostData.DamageMultiplier}, dmgBonus={hostData.DamageBonus}");
            }
            else if (bc != null)
            {
                // Host data missing — zero out tallies to avoid stale data
                _bcUpdater.SetPegMultiplierDamageTally(bc, 0);
                _bcUpdater.SetCriticalHitCount(0);
                _bcUpdater.SetNumPegsHit(bc, 0);
                _bcUpdater.SetCactusDamageTally(bc, 0);
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
                catch (Exception ex)
                {
                    _log.LogWarning($"[CoopSubs] Failed to set host target: {ex.Message}");
                }
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
                        _orbApplier.RestoreAttackFromPrefab(bc, orbPrefab, "host");
                    }
                    else
                    {
                        _log.LogWarning($"[CoopSubs] ALL_DONE: AssetLoading.GetOrbPrefab returned null for '{hostAttackData.OrbPrefabName}'");
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[CoopSubs] Failed to restore host Attack: {ex.Message}");
                }
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
            //
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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

        var activeSlot = _coopStateManager.ActivePlayerSlot;
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
        var bc = _bcUpdater.GetBattleController();
        if (bc != null)
        {
            try
            {
                _bcUpdater.ResetShotTallies(bc);
            }
            catch (Exception ex) { _log.LogWarning($"[CoopSubs] Skip: tally reset failed: {ex.Message}"); }

            // Destroy the aim ball and zero the remaining counter so physics
            // doesn't fire a free shot once we return control.
            try
            {
                _bcUpdater.DestroyActivePachinkoBall(bc);
                _bcUpdater.SetRemainingPachinkoBalls(bc, 0);
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[CoopSubs] Skip: clear ball failed: {ex.Message}");
            }
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
                _bcUpdater.SetBattleState(BattleController.BattleState.PRE_ATTACK_SPAWN_CHECK);
                _log.LogInfo("[CoopSubs] Skip: forced BattleState -> PRE_ATTACK_SPAWN_CHECK");
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[CoopSubs] Skip: state transition failed: {ex.Message}");
            }
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
        if (!_mode.IsHosting)
        {
            return;
        }

        if (_coopStateManager.TotalPlayerCount < 2)
        {
            return;
        }

        // Guard against re-entrant OnVictory firings (boss-kill BossHeal cascade
        // re-triggers CompleteVictory; without this guard PostBattleStartEvent
        // gets spammed every ~250ms, softlocking the host and lagging everyone).
        if (_victoryHandledThisBattle)
        {
            return;
        }

        _victoryHandledThisBattle = true;

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
            if (services?.TryResolve<IGameEventRegistry>(out var registry) != true)
            {
                return;
            }

            // Clear previous reward tracking state
            CoopRewardState.PendingSentRewardChoices.Clear();
            CoopRewardState.ClientRewardChoicesReceived.Clear();
            CoopRewardState.HostRewardPhaseActive = true;
            CoopRewardState.HostRewardsDone = false;
            CoopRewardState.PendingPostBattleController = null;

            var rewardClientCount = 0;
            foreach (var kvp in _coopStateManager.PlayerStates)
            {
                if (kvp.Key == 0)
                {
                    continue; // Host picks rewards via the normal game UI
                }

                rewardClientCount++;
            }

            CoopRewardState.TotalRewardClientsExpected = rewardClientCount;

            // Host-authoritative per-slot orb-reward rolls. Without this, every
            // player sees the same orb suggestions because BattleUpgradeCanvas
            // pulls from the shared seededBattleData. We roll a fresh list per
            // slot here, store the host's own list locally, and broadcast each
            // non-host slot's list to its targeted client. The patch on
            // PopulateSuggestionOrbs.GenerateAddableOrbs reads from
            // CoopRewardState.PerSlotOrbChoices to populate the buttons.
            try
            {
                GenerateAndBroadcastPerSlotOrbChoices(registry);
            }
            catch (Exception orbEx)
            {
                _log.LogWarning($"[CoopSubs] OnVictory per-slot orb generation failed: {orbEx.Message}");
            }

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
    // PER-SLOT ORB REWARD ROLLS (host-authoritative)
    // =========================================================================

    private const Relics.RelicEffect RELIC_ADDITIONAL_ORB_RELIC_OPTIONS = Relics.RelicEffect.ADDITIONAL_ORB_RELIC_OPTIONS;
    private const Relics.RelicEffect RELIC_ADDITIONAL_PEGLIN_CHOICES = Relics.RelicEffect.ADDITIONAL_PEGLIN_CHOICES;

    /// <summary>
    /// Roll a fresh orb-reward list per slot using the same probability mix
    /// the native PopulateSuggestionOrbs uses (60% common, 30% uncommon, 10%
    /// rare). Each slot's count uses that slot's owned relics for the +1
    /// bonuses (Eye of Turtle / Curiosity etc.). Stores the host's slot-0
    /// list in CoopRewardState and broadcasts each non-host slot's list to
    /// the targeted client.
    /// </summary>
    private void GenerateAndBroadcastPerSlotOrbChoices(IGameEventRegistry registry)
    {
        var deckMgr = Resources.FindObjectsOfTypeAll<DeckManager>().FirstOrDefault();
        if (deckMgr == null)
        {
            _log.LogWarning("[CoopSubs] GenerateAndBroadcastPerSlotOrbChoices: no DeckManager found");
            return;
        }

        var commonPool = deckMgr.CommonOrbPool;
        var uncommonPool = deckMgr.UncommonOrbPool;
        var rarePool = deckMgr.RareOrbPool;
        if (commonPool == null || commonPool.Count == 0)
        {
            _log.LogWarning("[CoopSubs] GenerateAndBroadcastPerSlotOrbChoices: empty CommonOrbPool");
            return;
        }

        CoopRewardState.PerSlotOrbChoices.Clear();

        foreach (var kvp in _coopStateManager.PlayerStates)
        {
            var slot = kvp.Key;
            var state = kvp.Value;
            if (state == null || !state.IsInitialized)
            {
                continue;
            }

            var count = 3;
            if (SlotHasRelic(state, RELIC_ADDITIONAL_ORB_RELIC_OPTIONS))
            {
                count++;
            }

            if (SlotHasRelic(state, RELIC_ADDITIONAL_PEGLIN_CHOICES))
            {
                count++;
            }

            var picked = new List<string>();
            var safety = 0;
            while (picked.Count < count && safety++ < 200)
            {
                List<GameObject> pool;
                var roll = UnityEngine.Random.value;
                if (roll <= 0.6f)
                {
                    pool = commonPool;
                }
                else if (roll <= 0.9f && uncommonPool != null && uncommonPool.Count > 0)
                {
                    pool = uncommonPool;
                }
                else if (rarePool != null && rarePool.Count > 0)
                {
                    pool = rarePool;
                }
                else
                {
                    pool = commonPool;
                }

                var prefab = pool[UnityEngine.Random.Range(0, pool.Count)];
                if (prefab == null)
                {
                    continue;
                }

                // Floor-based promotion to next-level prefab (mirrors native logic).
                if (UnityEngine.Random.value < (float)(StaticGameData.totalFloorCount - 5) / 100f)
                {
                    var attack = prefab.GetComponent<Battle.Attacks.Attack>();
                    if (attack != null && attack.NextLevelPrefab != null)
                    {
                        prefab = attack.NextLevelPrefab;
                        if (UnityEngine.Random.value < (float)(StaticGameData.totalFloorCount - 5) / 200f)
                        {
                            var attack2 = prefab.GetComponent<Battle.Attacks.Attack>();
                            if (attack2 != null && attack2.NextLevelPrefab != null)
                            {
                                prefab = attack2.NextLevelPrefab;
                            }
                        }
                    }
                }

                if (picked.Contains(prefab.name))
                {
                    continue;
                }

                picked.Add(prefab.name);
            }

            CoopRewardState.PerSlotOrbChoices[slot] = picked;
            _log.LogInfo($"[CoopSubs] Per-slot orb roll slot={slot} count={picked.Count}: {string.Join(",", picked)}");

            if (slot == 0)
            {
                continue; // Host reads its own list locally.
            }

            registry.Dispatch(new CoopOrbRewardChoicesEvent
            {
                TargetSlotIndex = slot,
                OrbPrefabNames = picked,
            });
        }
    }

    private static bool SlotHasRelic(CoopPlayerState state, Relics.RelicEffect effect)
    {
        if (state?.OwnedRelics == null)
        {
            return false;
        }

        foreach (var r in state.OwnedRelics)
        {
            if (r != null && r.Effect == (int)effect)
            {
                return true;
            }
        }

        return false;
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
        if (!_mode.IsHosting)
        {
            return;
        }

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
        var wasTheirTurn = _turnManager.RemovePlayer(slotIndex);

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
            var bc = _bcUpdater.GetBattleController();

            if (_accumulatedShotData.TryGetValue(0, out var hostData) && bc != null)
            {
                _bcUpdater.SetPegMultiplierDamageTally(bc, hostData.PegMultiplierDamageTally);
                _bcUpdater.SetCriticalHitCount(hostData.CriticalHitCount);
                _bcUpdater.SetNumPegsHit(bc, hostData.NumPegsHit);
                _bcUpdater.SetCactusDamageTally(bc, hostData.CactusDamageTally);
            }

            if (_coopStateManager.ActivePlayerSlot != 0)
            {
                _coopStateManager.SwapToPlayer(0);
            }

            BroadcastTurnChange();
            _log.LogInfo($"[CoopSubs] Disconnect during turn: all remaining players done, per-player resolution active");
        }
        else if (_turnManager.Phase == TurnPhase.PLAYER_AIMING)
        {
            // Next player needs to shoot. Swap to the next player's state and
            // redirect BattleController back to AWAITING_SHOT.
            var nextSlot = _turnManager.CurrentPlayerSlot;

            _coopStateManager.SwapToPlayer(nextSlot);

            // Disconnect DeckInfoManager callbacks to avoid corrupting host UI
            DeckManager.BallDrawn savedOnBallUsed = null;
            DeckManager.Shuffled savedOnDeckShuffled = null;
            DeckManager.PersistBallUsed savedOnPersistBallUsed = null;
            try
            {
                savedOnBallUsed = DeckManager.onBallUsed;
                savedOnDeckShuffled = DeckManager.onDeckShuffled;
                savedOnPersistBallUsed = DeckManager.onPersistBallUsed;
                DeckManager.onBallUsed = _ => { };
                DeckManager.onDeckShuffled = _ => { };
                DeckManager.onPersistBallUsed = () => { };
            }
            catch (Exception cbEx)
            {
                _log.LogWarning($"[CoopSubs] Disconnect: failed to save DeckManager callbacks: {cbEx.Message}");
            }

            _deckMgr.EnsureBattleDeckPopulated("disconnect swap");

            // Redirect battle state back to AWAITING_SHOT
            _bcUpdater.SetBattleState(BattleController.BattleState.AWAITING_SHOT);

            // Manually trigger DrawBall for the next player
            var bc = _bcUpdater.GetBattleController();
            try
            {
                if (bc != null)
                {
                    _bcUpdater.InvokeDrawBall(bc);
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
                    if (savedOnBallUsed != null)
                    {
                        DeckManager.onBallUsed = savedOnBallUsed;
                    }

                    if (savedOnDeckShuffled != null)
                    {
                        DeckManager.onDeckShuffled = savedOnDeckShuffled;
                    }

                    if (savedOnPersistBallUsed != null)
                    {
                        DeckManager.onPersistBallUsed = savedOnPersistBallUsed;
                    }
                }
                catch (Exception restoreEx)
                {
                    _log.LogWarning($"[CoopSubs] Disconnect: failed to restore DeckManager callbacks: {restoreEx.Message}");
                }
            }

            BroadcastTurnChange();

            try
            {
                _syncService.SyncAll("DisconnectTurnSwap");
            }
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

    // =========================================================================
    // PER-PLAYER DAMAGE RESOLUTION — accessed by DoAttack Harmony prefix
    // =========================================================================

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
        if (inst == null)
        {
            return null;
        }

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
                HasTargetedSplash = d.HasTargetedSplash,
                HasTargetedHitAll = d.HasTargetedHitAll,
            });
        }

        inst._accumulatedShotData.Clear();
        return result;
    }

    /// <summary>
    /// Returns ALL players' accumulated shot data for the pending damage overlay.
    /// Does NOT clear the data — called repeatedly during shots.
    /// </summary>
    internal static List<PendingDamagePreviewEvent.DamageEntry> GetAccumulatedDamageEntries()
    {
        var inst = Instance;
        if (inst == null)
        {
            return null;
        }

        var result = new List<PendingDamagePreviewEvent.DamageEntry>();
        foreach (var kvp in inst._accumulatedShotData)
        {
            var d = kvp.Value;
            if (d.IsHeal || d.PrecomputedDamage <= 0)
            {
                continue;
            }

            result.Add(new PendingDamagePreviewEvent.DamageEntry
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
    /// Clean up the temporary orb instance after DoAttack finishes.
    /// Called from the DoAttack postfix in MultiplayerClientPatches.
    /// </summary>
    internal static void CleanupTempOrb()
    {
        Instance?._orbApplier?.CleanupTempOrb();
    }

    /// <summary>
    /// Broadcast the current turn state to all clients.
    /// </summary>
    private void BroadcastTurnChange()
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var eventRegistry) != true)
            {
                return;
            }

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
