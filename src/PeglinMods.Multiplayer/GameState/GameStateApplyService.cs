using System;
using System.Collections;
using BepInEx.Logging;
using HarmonyLib;
using Loading;
using PeglinMods.Multiplayer.GameState.Appliers;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Scene-aware state manager with message queuing.
///
/// Core principle: Client state converges to host state.
/// - Tracks the authoritative host scene (_hostScene)
/// - Queues snapshots that arrive for scenes the client isn't on yet
/// - ApplyBufferedAfterDelay NEVER triggers scene changes (prevents stale bounces)
/// - Scene changes only from fresh ApplyAll or NodeActivatedClientHandler
/// - Pending snapshots are applied as soon as the client reaches the right scene
/// </summary>
public class GameStateApplyService
{
    private readonly ManualLogSource _log;
    private readonly MapStateApplier _mapApplier;
    private readonly PlayerStateApplier _playerApplier;
    private readonly EnemyStateApplier _enemyApplier;
    private readonly PegboardStateApplier _pegboardApplier;
    private readonly DeckStateApplier _deckApplier;
    private readonly RelicStateApplier _relicApplier;
    private readonly EnemyIdentifier _enemyId;
    private readonly PegIdentifier _pegId;

    // --- Authoritative host state ---
    private string _hostScene = "";
    private long _hostSceneTimestamp;

    // --- Pending snapshot for a scene the client is transitioning to ---
    private FullGameStateSnapshot _pendingSnapshot;
    private string _pendingSnapshotScene = "";

    // --- Individual buffered snapshots (for partial updates) ---
    private PlayerStateSnapshot _latestPlayer;
    private MapStateSnapshot _latestMap;

    public GameStateApplyService(ManualLogSource log, EnemyIdentifier enemyId, PegIdentifier pegId)
    {
        _log = log;
        _mapApplier = new MapStateApplier(log);
        _playerApplier = new PlayerStateApplier(log);
        _enemyApplier = new EnemyStateApplier(log, enemyId);
        _pegboardApplier = new PegboardStateApplier(log, pegId);
        _deckApplier = new DeckStateApplier(log);
        _relicApplier = new RelicStateApplier(log);
        _enemyId = enemyId;
        _pegId = pegId;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// Reset all internal state on disconnect. Clears pending snapshots,
    /// buffered state, and host scene tracking.
    /// </summary>
    public void Reset()
    {
        _hostScene = "";
        _hostSceneTimestamp = 0;
        _pendingSnapshot = null;
        _pendingSnapshotScene = "";
        _latestPlayer = null;
        _latestMap = null;
        _navigationTriggered = false;
        _log.LogInfo("[ApplyService] State reset");
    }

    // =========================================================================
    // SCENE LOADED — apply pending state, start post-load coroutine
    // =========================================================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        _log.LogInfo($"[ApplyService] Scene loaded: '{scene.name}' — clearing GUID registries, hostScene='{_hostScene}'");
        _enemyId.Clear();
        _pegId.Clear();
        Patches.MultiplayerClientPatches.MapControllerStartCompleted = false;

        // Check if we have a pending snapshot for this scene
        if (_pendingSnapshot != null &&
            string.Equals(_pendingSnapshotScene, scene.name, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInfo($"[ApplyService] Applying PENDING snapshot for '{scene.name}' (queued while transitioning)");
            var pending = _pendingSnapshot;
            _pendingSnapshot = null;
            _pendingSnapshotScene = "";

            // Apply after a short delay for scene init
            var dispatcher = MultiplayerPlugin.Services?.TryResolve<MainThreadDispatcher>(out var d) == true ? d : null;
            dispatcher?.StartCoroutine(ApplyPendingAfterDelay(scene.name, pending));
        }
        else
        {
            // Normal post-scene-load: wait for init then apply what we can
            var dispatcher = MultiplayerPlugin.Services?.TryResolve<MainThreadDispatcher>(out var d) == true ? d : null;
            dispatcher?.StartCoroutine(ApplyAfterSceneLoad(scene.name));
        }
    }

    // =========================================================================
    // APPLY ALL — fresh snapshot from host (main entry point)
    // =========================================================================

    public void ApplyAll(FullGameStateSnapshot snapshot)
    {
        var clientScene = SceneManager.GetActiveScene().name;
        var hostScene = snapshot.Map?.ActiveScene ?? "";

        // Update authoritative host scene
        _hostScene = hostScene;
        _hostSceneTimestamp = snapshot.TimestampMs;

        _log.LogInfo($"[ApplyService] ApplyAll: clientScene='{clientScene}', hostScene='{hostScene}', " +
            $"enemies={snapshot.Enemies?.Enemies?.Count ?? 0}, pegs={snapshot.Pegboard?.TotalPegCount ?? 0}");

        try
        {
            // CASE 1: Client is on the same scene as host — apply everything
            if (string.Equals(clientScene, hostScene, StringComparison.OrdinalIgnoreCase))
            {
                // Apply map data (node types, static data) without scene transition
                if (snapshot.Map != null)
                {
                    _latestMap = snapshot.Map;
                    _mapApplier.Apply(snapshot.Map);
                }

                ApplyNonMapState(snapshot);
                _log.LogInfo("[ApplyService] Applied full state (same scene).");
                return;
            }

            // Event/interaction scenes — host is making choices, client should NOT
            // load these scenes or queue pending snapshots for them. Just apply
            // player/deck/relic state and let MapApplier show the waiting message.
            if (IsEventScene(hostScene))
            {
                _log.LogInfo($"[ApplyService] Host on event scene '{hostScene}' — applying state without scene transition");
                if (snapshot.Map != null)
                {
                    _latestMap = snapshot.Map;
                    _mapApplier.Apply(snapshot.Map);
                }
                ApplyNonMapState(snapshot);
                return;
            }

            // CASE 2: Client is on a DIFFERENT scene — queue snapshot and let MapApplier handle transition
            _pendingSnapshot = snapshot;
            _pendingSnapshotScene = hostScene;
            _log.LogInfo($"[ApplyService] Queued snapshot for '{hostScene}' (client on '{clientScene}')");

            // Let MapApplier trigger the scene transition (any direction is OK for FRESH data)
            if (snapshot.Map != null)
            {
                _latestMap = snapshot.Map;
                _mapApplier.Apply(snapshot.Map);
            }

            // Apply player state (works on any scene) — use coop-aware path
            ApplyPlayerFromSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _log.LogError($"[ApplyService] ApplyAll failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-apply the latest map node types from host. Called after MapController.Start
    /// which resets nodes to NONE via blocked GenerateRoomType.
    /// </summary>
    public void ReapplyLastMapState()
    {
        if (_latestMap?.Nodes == null || _latestMap.Nodes.Count == 0) return;
        _log.LogInfo($"[ApplyService] Re-applying {_latestMap.Nodes.Count} map nodes after MapController.Start");
        _mapApplier.Apply(_latestMap);
    }

    // =========================================================================
    // POST-SCENE-LOAD COROUTINES
    // =========================================================================

    /// <summary>
    /// Apply a pending snapshot that was queued while transitioning.
    /// Shorter delay since we know exactly what to apply.
    /// </summary>
    private IEnumerator ApplyPendingAfterDelay(string sceneName, FullGameStateSnapshot snapshot)
    {
        yield return null;

        if (sceneName == "Battle")
        {
            yield return null;
            yield return null;
            yield return WaitForEnemyCache();
            yield return new WaitForSeconds(0.3f);
        }
        else
        {
            // Map scenes: wait for MapController.Start to complete before applying
            // node types. Start resets all nodes to NONE (via blocked GenerateRoomType),
            // so we must apply AFTER it finishes. The postfix sets the flag.
            float timeout = Time.time + 5f;
            while (!Patches.MultiplayerClientPatches.MapControllerStartCompleted && Time.time < timeout)
                yield return null;
        }

        var currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != sceneName)
        {
            _log.LogWarning($"[ApplyService] Scene changed during pending apply delay: {sceneName} → {currentScene}");
            yield break;
        }

        _log.LogInfo($"[ApplyService] Applying pending snapshot for '{sceneName}'");
        DiagnosticLogger.DumpBattleState($"CLIENT_BeforePending_{sceneName}");

        // Apply map data (node types, static data) — no scene change since we're already here
        if (snapshot.Map != null)
        {
            _latestMap = snapshot.Map;
            SafeApply("Map(pending)", () => _mapApplier.Apply(snapshot.Map));
        }

        // Apply all non-map state
        ApplyNonMapState(snapshot);

        DiagnosticLogger.DumpBattleState($"CLIENT_AfterPending_{sceneName}");
    }

    /// <summary>
    /// Normal post-scene-load apply. No pending snapshot exists.
    /// Applies player state only. Does NOT trigger scene changes.
    /// </summary>
    private IEnumerator ApplyAfterSceneLoad(string sceneName)
    {
        yield return null;
        yield return null;
        yield return null;

        if (sceneName == "Battle")
        {
            yield return WaitForEnemyCache();
        }

        yield return new WaitForSeconds(0.5f);

        var currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != sceneName)
        {
            _log.LogWarning($"[ApplyService] Scene changed during delay: {sceneName} → {currentScene}");
            yield break;
        }

        _log.LogInfo($"[ApplyService] Post-scene-load apply for '{sceneName}' (no pending snapshot)");

        // Apply player state if available
        if (_latestPlayer != null)
            SafeApply("Player", () => _playerApplier.Apply(_latestPlayer));

        // Check if a pending snapshot arrived while we were waiting
        if (_pendingSnapshot != null &&
            string.Equals(_pendingSnapshotScene, currentScene, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInfo($"[ApplyService] Pending snapshot arrived during wait — applying now");
            var pending = _pendingSnapshot;
            _pendingSnapshot = null;
            _pendingSnapshotScene = "";

            if (pending.Map != null)
                SafeApply("Map(late-pending)", () => _mapApplier.Apply(pending.Map));

            ApplyNonMapState(pending);
        }
    }

    private IEnumerator WaitForEnemyCache()
    {
        var cache = AssetLoading.Instance?.EnemyPrefabs;
        int waitFrames = 0;
        while ((cache == null || cache.Count == 0) && waitFrames < 30)
        {
            yield return null;
            waitFrames++;
            cache = AssetLoading.Instance?.EnemyPrefabs;
        }
        if (cache != null && cache.Count > 0)
            _log.LogInfo($"[ApplyService] Enemy prefab cache ready: {cache.Count} entries (waited {waitFrames} frames)");
        else
            _log.LogWarning($"[ApplyService] Enemy prefab cache still empty after {waitFrames} frames");
    }

    // =========================================================================
    // APPLY NON-MAP STATE — enemies, pegs, deck, relics
    // =========================================================================

    private void ApplyNonMapState(FullGameStateSnapshot snapshot)
    {
        var currentScene = SceneManager.GetActiveScene().name;

        // In coop mode, each player has their own deck/relics — don't overwrite with host's
        var isCoop = UI.LobbyUI.GameStartReceived;

        // In coop, the Player snapshot contains the host's active player's data, which
        // may not be this client's player. Use PlayerSummaries to find our own health/gold.
        if (isCoop && snapshot.PlayerSummaries != null && snapshot.PlayerSummaries.Count > 0)
        {
            int mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
            bool found = false;
            foreach (var summary in snapshot.PlayerSummaries)
            {
                if (summary.SlotIndex == mySlot)
                {
                    var myPlayerState = new PlayerStateSnapshot
                    {
                        ActiveSlotIndex = mySlot,
                        CurrentHealth = summary.CurrentHealth,
                        MaxHealth = summary.MaxHealth,
                        Gold = summary.Gold,
                    };
                    // Preserve status effects and speedup from the generic snapshot if available
                    if (snapshot.Player != null)
                    {
                        myPlayerState.StatusEffects = snapshot.Player.StatusEffects;
                        myPlayerState.IsSpedUp = snapshot.Player.IsSpedUp;
                        myPlayerState.SpeedupLevel = snapshot.Player.SpeedupLevel;
                    }
                    SafeApply("Player(coop)", () => _playerApplier.Apply(myPlayerState));
                    found = true;
                    break;
                }
            }
            if (!found && snapshot.Player != null)
            {
                _log.LogWarning($"[ApplyService] Coop: could not find slot {mySlot} in PlayerSummaries, falling back to generic Player snapshot");
                SafeApply("Player", () => _playerApplier.Apply(snapshot.Player));
            }
        }
        else if (snapshot.Player != null)
        {
            SafeApply("Player", () => _playerApplier.Apply(snapshot.Player));
        }

        // Battle-specific state
        if (currentScene == "Battle")
        {
            if (snapshot.Enemies != null) SafeApply("Enemies", () => _enemyApplier.Apply(snapshot.Enemies));
            if (snapshot.Pegboard != null) SafeApply("Pegboard", () => _pegboardApplier.Apply(snapshot.Pegboard));
            if (!isCoop)
            {
                if (snapshot.Deck != null) SafeApply("Deck", () => _deckApplier.Apply(snapshot.Deck));
                if (snapshot.Relics != null) SafeApply("Relics", () => _relicApplier.Apply(snapshot.Relics));
            }
            else if (snapshot.Deck != null)
            {
                // In coop, don't sync the full deck (each player has their own).
                // But DO sync the active orb display so the client sees the correct
                // orb at the aimer position during the host's (or other player's) turn.
                SafeApply("Deck(coop-orb-only)", () => _deckApplier.ApplyActiveOrbOnly(snapshot.Deck));
            }
            VerifyConsistency(snapshot);

            // Post-battle navigation: trigger the game's own setup on the client
            TriggerNavigationIfNeeded(snapshot);
        }
        else
        {
            if (!isCoop)
            {
                // Non-battle scenes: still sync deck and relics (they're global) in spectator mode
                if (snapshot.Deck != null) SafeApply("Deck", () => _deckApplier.Apply(snapshot.Deck));
                if (snapshot.Relics != null) SafeApply("Relics", () => _relicApplier.Apply(snapshot.Relics));
            }
            _log.LogInfo($"[ApplyService] Non-battle scene '{currentScene}': applied player/deck/relics, skipped enemies/pegs{(isCoop ? " (coop: deck/relic sync skipped)" : "")}");
        }
    }

    // =========================================================================
    // INDIVIDUAL SNAPSHOT APPLIERS — from per-type client handlers
    // =========================================================================

    public void ApplyMapState(MapStateSnapshot snapshot)
    {
        _latestMap = snapshot;
        // Update host scene tracking
        if (!string.IsNullOrEmpty(snapshot.ActiveScene))
        {
            _hostScene = snapshot.ActiveScene;
        }
        SafeApply("Map", () => _mapApplier.Apply(snapshot));
    }

    public void ApplyPlayerState(PlayerStateSnapshot snapshot)
    {
        _latestPlayer = snapshot;
        SafeApply("Player", () => _playerApplier.Apply(snapshot));
    }

    public void ApplyEnemyState(EnemyStateSnapshot snapshot)
    {
        if (SceneManager.GetActiveScene().name != "Battle")
        {
            _log.LogInfo("[ApplyService] Buffered enemy state (not on Battle scene)");
            return;
        }
        SafeApply("Enemies", () => _enemyApplier.Apply(snapshot));
    }

    public void ApplyPegboardState(PegboardStateSnapshot snapshot)
    {
        if (SceneManager.GetActiveScene().name != "Battle")
        {
            _log.LogInfo("[ApplyService] Buffered pegboard state (not on Battle scene)");
            return;
        }
        SafeApply("Pegboard", () => _pegboardApplier.Apply(snapshot));
    }

    public void ApplyDeckState(DeckStateSnapshot snapshot)
    {
        SafeApply("Deck", () => _deckApplier.Apply(snapshot));
    }

    public void ApplyRelicState(RelicStateSnapshot snapshot)
    {
        SafeApply("Relics", () => _relicApplier.Apply(snapshot));
    }

    // =========================================================================
    // CONSISTENCY CHECK
    // =========================================================================

    private void VerifyConsistency(FullGameStateSnapshot snapshot)
    {
        try
        {
            if (SceneManager.GetActiveScene().name != "Battle") return;

            if (snapshot.Enemies?.Enemies != null)
            {
                var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
                int clientEnemies = em?.Enemies?.Count ?? 0;
                int hostEnemies = snapshot.Enemies.Enemies.Count;
                if (clientEnemies != hostEnemies)
                {
                    _log.LogWarning($"[Consistency] ENEMY COUNT MISMATCH: host={hostEnemies}, client={clientEnemies}");
                    _enemyId.DumpState("ConsistencyCheck");
                }
                else
                {
                    _log.LogInfo($"[Consistency] Enemies OK: {clientEnemies} match");
                }
            }

            if (snapshot.Pegboard?.Pegs != null)
            {
                var bc2 = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
                var pm = bc2?.pegManager;
                int clientPegs = 0;
                if (pm?.allPegs != null)
                {
                    foreach (var p in pm.allPegs)
                    {
                        if (p == null || !p.gameObject.activeSelf || p.pegType == Peg.PegType.DESTROYED) continue;
                        try { if (!p.IsDisabled()) clientPegs++; } catch { }
                    }
                }
                // Include bombs in client count
                var bombsF = HarmonyLib.AccessTools.Field(typeof(Battle.PegManager), "_bombs");
                var cbombs = bombsF?.GetValue(pm) as System.Collections.Generic.List<Bomb>;
                if (cbombs != null)
                {
                    foreach (var b in cbombs)
                    {
                        if (b == null || !b.gameObject.activeSelf || b.pegType == Peg.PegType.DESTROYED) continue;
                        try { if (!b.IsDisabled()) clientPegs++; } catch { }
                    }
                }
                // Host count: not destroyed AND not popped (same criteria as client)
                int hostActivePegs = 0;
                foreach (var p in snapshot.Pegboard.Pegs)
                    if (!p.IsDestroyed && !p.IsCleared) hostActivePegs++;

                if (System.Math.Abs(clientPegs - hostActivePegs) > 5)
                    _log.LogWarning($"[Consistency] PEG COUNT MISMATCH: host_active={hostActivePegs}, client_active={clientPegs}");
                else
                    _log.LogInfo($"[Consistency] Pegs OK: host={hostActivePegs}, client={clientPegs}");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[Consistency] Check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Event/interaction scenes where the host makes choices.
    /// The client should NOT load these — it stays on its current scene
    /// and shows a waiting message.
    /// </summary>
    private static bool IsEventScene(string scene) =>
        scene == "Treasure" || scene == "TextScenario" ||
        scene == "ShopScenario" || scene == "PegMinigame" ||
        scene == "ForestWinScene" || scene == "CastleWinScene" ||
        scene == "FinalWinScene" || scene == "CoreWinScene" ||
        scene == "RunSummary";

    // =========================================================================
    // POST-BATTLE NAVIGATION
    // =========================================================================

    private bool _navigationTriggered;

    /// <summary>
    /// When the host enters post-battle navigation, configure the client's slot visuals
    /// and update the ball sprite to NavigationOrb. Uses synced child node data from the
    /// MapStateSnapshot (cached on the host before currentNode is destroyed).
    /// Also resets pegs to match host's navigation state.
    /// </summary>
    private void TriggerNavigationIfNeeded(FullGameStateSnapshot snapshot)
    {
        var battleState = snapshot.Enemies?.BattleStateName;
        bool isNav = battleState == "NAVIGATION" || battleState == "AWAITING_POST_BATTLE_CONTROLLER";

        if (!isNav)
        {
            _navigationTriggered = false;
            return;
        }

        if (_navigationTriggered) return;

        try
        {
            // Configure slot visuals from synced child node data
            var navTypes = snapshot.Map?.NavChildNodeTypes;
            if (navTypes != null && navTypes.Count > 0 && (snapshot.Map?.IsNavigating ?? false))
            {
                _mapApplier.ApplyNavigationSlots(navTypes);
            }

            // Update ClientBallRenderer to show NavigationOrb sprite
            var activeOrb = snapshot.Deck?.CurrentOrb;
            if (!string.IsNullOrEmpty(activeOrb) && activeOrb.Contains("NavigationOrb"))
            {
                ClientBallRenderer.Instance?.OnOrbDrawn(activeOrb);
            }

            // Reset pegs for navigation — the host calls PreparePegsForNavigation()
            // which resets all pegs to their base state (removes crit highlight, etc.)
            try
            {
                var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
                if (bc != null)
                {
                    bc.PreparePegsForNavigation();
                    bc.RemoveClearedPegs();
                }
            }
            catch { }

            _navigationTriggered = true;
            _log.LogInfo($"[ApplyService] Navigation triggered: {navTypes?.Count ?? 0} child nodes, orb={activeOrb}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[ApplyService] TriggerNavigation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply player state from a full snapshot, using PlayerSummaries in coop mode
    /// to find this client's own health/gold instead of the host's active player.
    /// </summary>
    private void ApplyPlayerFromSnapshot(FullGameStateSnapshot snapshot)
    {
        var isCoop = UI.LobbyUI.GameStartReceived;

        if (isCoop && snapshot.PlayerSummaries != null && snapshot.PlayerSummaries.Count > 0)
        {
            int mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
            foreach (var summary in snapshot.PlayerSummaries)
            {
                if (summary.SlotIndex == mySlot)
                {
                    var myPlayerState = new PlayerStateSnapshot
                    {
                        ActiveSlotIndex = mySlot,
                        CurrentHealth = summary.CurrentHealth,
                        MaxHealth = summary.MaxHealth,
                        Gold = summary.Gold,
                    };
                    if (snapshot.Player != null)
                    {
                        myPlayerState.StatusEffects = snapshot.Player.StatusEffects;
                        myPlayerState.IsSpedUp = snapshot.Player.IsSpedUp;
                        myPlayerState.SpeedupLevel = snapshot.Player.SpeedupLevel;
                    }
                    SafeApply("Player(coop-xscene)", () => _playerApplier.Apply(myPlayerState));
                    return;
                }
            }
        }

        if (snapshot.Player != null)
            SafeApply("Player", () => _playerApplier.Apply(snapshot.Player));
    }

    private void SafeApply(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.LogError($"[ApplyService] {name} failed: {ex.Message}"); }
    }
}
