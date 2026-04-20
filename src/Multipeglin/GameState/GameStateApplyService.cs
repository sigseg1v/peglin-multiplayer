using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using Loading;
using Multipeglin.GameState.Appliers;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multipeglin.GameState;

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

    public GameStateApplyService(ManualLogSource log, EnemyIdentifier enemyId, PegIdentifier pegId, OrbIdentifier orbId)
    {
        _log = log;
        _mapApplier = new MapStateApplier(log);
        _playerApplier = new PlayerStateApplier(log);
        _enemyApplier = new EnemyStateApplier(log, enemyId);
        _pegboardApplier = new PegboardStateApplier(log, pegId);
        _deckApplier = new DeckStateApplier(log, orbId);
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

        // Reset TextScenario state on scene transitions
        TextScenarioHoverTracker.Reset();
        Patches.MultiplayerClientPatches.AllowTextScenarioNavigation = false;
        _wasNavigating = false;
        _lastAppliedHighlightIndex = -1;
        _lastAppliedSubtitle = null;
        _lastAppliedResponseCount = -1;
        if (UI.MirrorEventUI.IsActive)
            UI.MirrorEventUI.Hide();

        // Reset shop/treasure/minigame/textscenario bypass flags on any scene transition
        Patches.MultiplayerClientPatches.AllowShopLogic = false;
        Patches.MultiplayerClientPatches.AllowTreasureLogic = false;
        Patches.MultiplayerClientPatches.AllowPegMinigameLogic = false;
        Patches.MultiplayerClientPatches.AllowTextScenarioLogic = false;
        Patches.MultiplayerClientPatches.ClientShopPurchases.Clear();
        Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent = false;
        Events.Handlers.Coop.CoopRewardState.ClientPegMinigameChoiceSent = false;
        Events.Handlers.Coop.CoopRewardState.ClientTextScenarioChoiceSent = false;

        var svc = MultiplayerPlugin.Services;
        IMultiplayerMode mpModeRef = null;
        svc?.TryResolve(out mpModeRef);
        bool isHosting = mpModeRef?.IsHosting == true;
        bool isSpectating = mpModeRef?.IsSpectating == true;

        // Shop scene: initialize wait-for-all on host, enable shopping on client.
        // NOTE: AllChoicesComplete lingers from prior phases (e.g. text_scenario)
        // and suppresses the waiting overlay in CoopRewardUI if not cleared — that
        // was the root cause of the "Exit Store button does nothing" bug. Reset
        // every shop-related state here on both sides.
        if (scene.name == "ShopScenario")
        {
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
            Events.Handlers.Coop.CoopRewardState.ShopCompletionProceeded = false;
            Events.Handlers.Coop.CoopRewardState.ClientShopChoiceSent = false;
            Events.Handlers.Coop.CoopRewardState.ShopAwaitingHostNavigation = false;

            if (isHosting && svc.TryResolve<CoopStateManager>(out var coopMgr))
            {
                Events.Handlers.Coop.CoopRewardState.ShopPhaseActive = true;
                Events.Handlers.Coop.CoopRewardState.HostShopDone = false;
                Events.Handlers.Coop.CoopRewardState.ClientShopChoicesReceived.Clear();
                Events.Handlers.Coop.CoopRewardState.TotalShopClientsExpected = coopMgr.TotalPlayerCount - 1;
                Events.Handlers.Coop.CoopRewardState.PendingShopManager = null;
                _log.LogInfo($"[ApplyService] Shop phase initialized — expecting {coopMgr.TotalPlayerCount - 1} client(s)");
            }

            if (isSpectating)
            {
                // The client needs ShopPhaseActive flagged too so the overlay's
                // ShowWaiting() picks the right text ("finish shopping" vs generic).
                Events.Handlers.Coop.CoopRewardState.ShopPhaseActive = true;
                Patches.MultiplayerClientPatches.AllowShopLogic = true;
                Patches.MultiplayerClientPatches.AllowCurrencySync = true;
                Patches.MultiplayerClientPatches.AllowRelicSync = true;
                Patches.MultiplayerClientPatches.ClientShopStartGold =
                    Currency.CurrencyManager.Instance?.GoldAmount ?? 0;
                _log.LogInfo("[ApplyService] Client shop mode enabled — AllowShopLogic=true");
            }
        }

        // PegMinigame scene: initialize wait-for-all on host, enable interactive play on client
        if (scene.name == "PegMinigame")
        {
            if (isHosting && svc.TryResolve<CoopStateManager>(out var coopMgr3))
            {
                Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive = true;
                Events.Handlers.Coop.CoopRewardState.HostPegMinigameDone = false;
                Events.Handlers.Coop.CoopRewardState.ClientPegMinigameChoicesReceived.Clear();
                Events.Handlers.Coop.CoopRewardState.TotalPegMinigameClientsExpected = coopMgr3.TotalPlayerCount - 1;
                Events.Handlers.Coop.CoopRewardState.PendingPegMinigameManager = null;
                _log.LogInfo($"[ApplyService] PegMinigame phase initialized — expecting {coopMgr3.TotalPlayerCount - 1} client(s)");
            }

            if (isSpectating)
            {
                // Flag phase active on the client so ShowWaiting() picks descriptive
                // "finish the event" text once the client sends their completion.
                Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive = true;
                Patches.MultiplayerClientPatches.AllowPegMinigameLogic = true;
                _log.LogInfo("[ApplyService] Client PegMinigame mode enabled — AllowPegMinigameLogic=true");
            }
        }

        // Treasure scene: initialize wait-for-all on host, enable native relic UI on client
        if (scene.name == "Treasure")
        {
            Events.Handlers.Coop.CoopRewardState.TreasureAwaitingHostNavigation = false;

            if (isHosting && svc.TryResolve<CoopStateManager>(out var coopMgr2))
            {
                Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = true;
                Events.Handlers.Coop.CoopRewardState.HostTreasureDone = false;
                Events.Handlers.Coop.CoopRewardState.ClientTreasureChoicesReceived.Clear();
                Events.Handlers.Coop.CoopRewardState.TotalTreasureClientsExpected = coopMgr2.TotalPlayerCount - 1;
                Events.Handlers.Coop.CoopRewardState.PendingChestController = null;
                _log.LogInfo($"[ApplyService] Treasure phase initialized — expecting {coopMgr2.TotalPlayerCount - 1} client(s)");
            }

            if (isSpectating)
            {
                // Flag phase active on the client so ShowWaiting() picks the
                // "choose a relic" text once the client sends their completion.
                Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = true;
                Patches.MultiplayerClientPatches.AllowTreasureLogic = true;
                Patches.MultiplayerClientPatches.AllowRelicSync = true;
                _log.LogInfo("[ApplyService] Client treasure mode enabled — AllowTreasureLogic=true");
            }
        }

        // TextScenario scene: initialize wait-for-all on host, enable native dialogue on client
        if (scene.name == "TextScenario")
        {
            // Clear cross-phase leakage from previous TextScenario / shop / etc.
            Events.Handlers.Coop.CoopRewardState.ClientTextScenarioChoiceSent = false;
            Events.Handlers.Coop.CoopRewardState.TextScenarioAwaitingHostNavigation = false;

            if (isHosting && svc.TryResolve<CoopStateManager>(out var coopMgr4))
            {
                Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive = true;
                Events.Handlers.Coop.CoopRewardState.HostTextScenarioDone = false;
                Events.Handlers.Coop.CoopRewardState.ClientTextScenarioChoicesReceived.Clear();
                Events.Handlers.Coop.CoopRewardState.TotalTextScenarioClientsExpected = coopMgr4.TotalPlayerCount - 1;
                Events.Handlers.Coop.CoopRewardState.PendingDialogueSystemScenario = null;
                _log.LogInfo($"[ApplyService] TextScenario phase initialized — expecting {coopMgr4.TotalPlayerCount - 1} client(s)");
            }

            if (isSpectating)
            {
                // Flag phase active on the client too so ShowWaiting() picks the
                // "finish the event" text while this player is still pre-choice or
                // has just sent their completion event.
                Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive = true;
                Patches.MultiplayerClientPatches.AllowTextScenarioLogic = true;
                Patches.MultiplayerClientPatches.AllowCurrencySync = true;
                Patches.MultiplayerClientPatches.AllowRelicSync = true;
                _log.LogInfo("[ApplyService] Client TextScenario mode enabled — AllowTextScenarioLogic=true");
            }
        }

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

            // Client is on an interactive scene (Shop, Treasure, TextScenario, PegMinigame)
            // but the host hasn't finished loading it yet — the host heartbeat still reports
            // the previous scene. Don't transition the client back; just stay put.
            if (IsClientInteractiveScene(clientScene))
            {
                _log.LogInfo($"[ApplyService] Client on interactive scene '{clientScene}' (host='{hostScene}') — skipping scene transition");
                // Don't apply player state during TextScenario — client is modifying its own state
                if (!Patches.MultiplayerClientPatches.AllowTextScenarioLogic)
                    ApplyPlayerFromSnapshot(snapshot);
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

        // During the native post-battle reward phase, the client's singletons are being
        // modified by BattleUpgradeCanvas. Don't overwrite with heartbeat data.
        if (isCoop && Events.Handlers.Coop.CoopRewardState.ClientInNativeRewardPhase)
        {
            _log.LogInfo("[ApplyService] Skipping player/deck sync — client in native reward phase");
            return;
        }

        // During TextScenario dialogue, the client is making independent choices
        // that modify DeckManager/health/gold. Don't overwrite with heartbeat data.
        if (isCoop && Patches.MultiplayerClientPatches.AllowTextScenarioLogic)
        {
            _log.LogInfo("[ApplyService] Skipping player/deck sync — client in TextScenario dialogue");
            return;
        }

        // In coop, the Player snapshot contains the host's active player's data, which
        // may not be this client's player. Use PlayerSummaries to find our own health/gold.
        if (isCoop && snapshot.PlayerSummaries != null && snapshot.PlayerSummaries.Count > 0)
        {
            // Cache host player name for spectator banners
            foreach (var s in snapshot.PlayerSummaries)
            {
                if (s.SlotIndex == 0 && !string.IsNullOrEmpty(s.PlayerName))
                {
                    Appliers.MapStateApplier.HostPlayerName = s.PlayerName;
                    break;
                }
            }

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
                    // Use per-player status effects from the summary (each player's own effects)
                    if (summary.StatusEffects != null && summary.StatusEffects.Count > 0)
                    {
                        myPlayerState.StatusEffects = summary.StatusEffects;
                    }
                    // Preserve speedup from the generic snapshot if available
                    if (snapshot.Player != null)
                    {
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
            else
            {
                // In coop, apply this client's own deck from AllDecks.
                // The host sends per-player deck data; we pick our slot.
                Snapshots.DeckStateSnapshot myDeck = null;
                int mySlotIdx = -1;
                if (snapshot.AllDecks != null)
                {
                    var services = MultiplayerPlugin.Services;
                    if (services?.TryResolve<Multiplayer.PlayerRegistry>(out var registry) == true
                        && registry.LocalSlot != null)
                    {
                        mySlotIdx = registry.LocalSlot.SlotIndex;
                        snapshot.AllDecks.TryGetValue(mySlotIdx, out myDeck);
                    }
                }

                if (myDeck != null)
                {
                    var deckOrbs = myDeck.CompleteDeck != null ? string.Join(", ", myDeck.CompleteDeck.Select(o => o.Name)) : "NULL";
                    _log.LogInfo($"[ApplyService] Coop deck for mySlot={mySlotIdx}: {myDeck.CompleteDeck?.Count ?? 0} orbs [{deckOrbs}] shuffled={myDeck.ShuffledOrder?.Count ?? 0}");
                    SafeApply("Deck(coop-own)", () => _deckApplier.Apply(myDeck));
                }
                else if (snapshot.Deck != null)
                {
                    _log.LogWarning($"[ApplyService] Coop: AllDecks missing slot {mySlotIdx}, falling back to orb-only");
                    SafeApply("Deck(coop-orb-only)", () => _deckApplier.ApplyActiveOrbOnly(snapshot.Deck));
                }

                // Aimer-orb sync for coop: AllDecks[mySlot].CurrentOrb is the
                // player's OWN next orb (null for inactive slots). During the
                // post-battle navigation phase the host is holding a
                // NavigationOrb on its own slot — we need the client's aimer to
                // show the navigation orb regardless of which slot is active.
                // snapshot.Deck.CurrentOrb tracks the host's live active orb
                // (e.g. "NavigationOrb(Clone)"), so apply it on top.
                var hostActiveOrb = snapshot.Deck?.CurrentOrb;
                if (!string.IsNullOrEmpty(hostActiveOrb) && hostActiveOrb.Contains("NavigationOrb"))
                {
                    SafeApply("Deck(coop-nav-orb)", () => _deckApplier.ApplyActiveOrbOnly(snapshot.Deck));
                }
            }
            // In coop, use the heartbeat to keep IsMyTurn in sync.
            // TurnChangeEvent is a one-shot that can be missed if the client
            // loads the Battle scene after the host already broadcast it.
            // The heartbeat is authoritative — converge every 2 seconds.
            if (isCoop && snapshot.TotalPlayerCount >= 2)
            {
                try
                {
                    int mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
                    var battleState = snapshot.Enemies?.BattleStateName ?? "";
                    bool hostWantsMyShot = snapshot.ActivePlayerSlot == mySlot
                        && (battleState == "AWAITING_SHOT" || battleState == "SPAWNING");

                    var handler = Events.Handlers.Coop.TurnChangeClientHandler.LatestTurnState;
                    bool currentlyMyTurn = Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn;

                    // If the heartbeat says it's my turn but the one-shot event was missed, fix it
                    if (hostWantsMyShot && !currentlyMyTurn)
                    {
                        _log.LogInfo($"[ApplyService] Heartbeat: fixing IsMyTurn=true (slot={mySlot}, battleState={battleState}, activeSlot={snapshot.ActivePlayerSlot})");
                        Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn = true;
                        Events.Handlers.Coop.TurnChangeClientHandler.TurnMessage = "Your turn! Aim and shoot.";
                        Patches.MultiplayerClientPatches.ClientShotSentThisTurn = false;
                    }
                    // If the heartbeat says it's NOT my turn but the client thinks it is, fix it
                    else if (!hostWantsMyShot && currentlyMyTurn
                        && battleState != "" && battleState != "SHOULD_SPAWN")
                    {
                        Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn = false;
                        Events.Handlers.Coop.TurnChangeClientHandler.TurnMessage = "";
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[ApplyService] Heartbeat turn sync failed: {ex.Message}");
                }
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

        // TextScenario spectator UI — driven by heartbeat
        ApplyTextScenarioState(snapshot);
    }

    // =========================================================================
    // TEXT SCENARIO SPECTATOR — sync dialogue text/responses/hover to client
    // =========================================================================

    private bool _wasNavigating;
    private string _lastAppliedSubtitle;
    private int _lastAppliedResponseCount = -1;

    private void ApplyTextScenarioState(FullGameStateSnapshot snapshot)
    {
        var ts = snapshot.TextScenario;
        var currentScene = SceneManager.GetActiveScene().name;

        if (ts == null || currentScene != "TextScenario")
        {
            _wasNavigating = false;
            _lastAppliedSubtitle = null;
            _lastAppliedResponseCount = -1;
            return;
        }

        // Sync the host's dialogue text, responses, and hover highlight to client
        if (ts.IsActive)
        {
            ApplyDialogueText(ts.SubtitleText, ts.Responses);
            ApplyResponseHighlight(ts.HighlightedIndex);
        }

        // Handle navigation transition: when host starts navigation phase,
        // activate the navigation controller on the client
        if (ts.IsNavigating && !_wasNavigating)
        {
            _log.LogInfo("[ApplyService] TextScenario navigation started — activating nav controller on client");
            ActivateClientNavigation();
        }
        _wasNavigating = ts.IsNavigating;
    }

    /// <summary>
    /// Update the client's native dialogue UI subtitle text and response buttons
    /// to match the host's current state. This keeps the client in sync as the
    /// host advances through the conversation.
    /// </summary>
    private void ApplyDialogueText(string subtitleText, List<string> responses)
    {
        try
        {
            bool subtitleChanged = subtitleText != _lastAppliedSubtitle;
            bool responsesChanged = (responses?.Count ?? 0) != _lastAppliedResponseCount;

            if (!subtitleChanged && !responsesChanged) return;

            var dialogueUI = UnityEngine.Object.FindObjectOfType<PixelCrushers.DialogueSystem.StandardDialogueUI>();
            if (dialogueUI == null) return;

            // Update NPC subtitle text
            if (subtitleChanged && !string.IsNullOrEmpty(subtitleText))
            {
                var npcPanel = dialogueUI.conversationUIElements?.defaultNPCSubtitlePanel;
                if (npcPanel?.subtitleText != null)
                {
                    npcPanel.subtitleText.text = subtitleText;
                    _lastAppliedSubtitle = subtitleText;
                }
            }

            // Update response buttons
            if (responsesChanged && responses != null)
            {
                var menuPanel = dialogueUI.conversationUIElements?.defaultMenuPanel;
                if (menuPanel?.buttons != null)
                {
                    for (int i = 0; i < menuPanel.buttons.Length; i++)
                    {
                        var btn = menuPanel.buttons[i];
                        if (btn == null) continue;

                        if (i < responses.Count && !string.IsNullOrEmpty(responses[i]))
                        {
                            btn.gameObject.SetActive(true);
                            btn.text = responses[i];
                        }
                        else if (btn.gameObject.activeInHierarchy && btn.isVisible)
                        {
                            // Hide extra buttons that the host no longer shows
                            btn.gameObject.SetActive(false);
                        }
                    }
                    _lastAppliedResponseCount = responses.Count;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[ApplyService] ApplyDialogueText failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Highlight the response button at the given index on the client's native
    /// Dialogue System UI, mirroring the host's hover state.
    /// </summary>
    private int _lastAppliedHighlightIndex = -1;

    private void ApplyResponseHighlight(int highlightedIndex)
    {
        if (highlightedIndex == _lastAppliedHighlightIndex) return;
        _lastAppliedHighlightIndex = highlightedIndex;

        try
        {
            var dialogueUI = UnityEngine.Object.FindObjectOfType<PixelCrushers.DialogueSystem.StandardDialogueUI>();
            if (dialogueUI == null) return;

            var menuPanel = dialogueUI.conversationUIElements?.defaultMenuPanel;
            if (menuPanel?.buttons == null) return;

            for (int i = 0; i < menuPanel.buttons.Length; i++)
            {
                var btn = menuPanel.buttons[i];
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.isVisible) continue;

                if (i == highlightedIndex && btn.button != null)
                {
                    // Use Unity's EventSystem to select this button, which triggers
                    // the native UI highlight state (color transition, animation, etc.)
                    var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                    if (eventSystem != null)
                        eventSystem.SetSelectedGameObject(btn.gameObject);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[ApplyService] ApplyResponseHighlight failed: {ex.Message}");
        }
    }

    private void ActivateClientNavigation()
    {
        try
        {
            var dss = UnityEngine.Object.FindObjectOfType<RNG.Scenarios.DialogueSystemScenario>();
            if (dss == null)
            {
                _log.LogWarning("[ApplyService] DialogueSystemScenario not found for navigation activation");
                return;
            }

            // Hide the dialogue text canvas — the host has moved to navigation
            var textCanvasField = HarmonyLib.AccessTools.Field(typeof(RNG.Scenarios.DialogueSystemScenario), "mainTextAnimatorCanvas");
            var textCanvas = textCanvasField?.GetValue(dss) as CanvasGroup;
            if (textCanvas != null)
                textCanvas.gameObject.SetActive(false);

            // Stop the client's conversation if still active
            if (PixelCrushers.DialogueSystem.DialogueManager.isConversationActive)
            {
                PixelCrushers.DialogueSystem.DialogueManager.StopConversation();
                _log.LogInfo("[ApplyService] Stopped client conversation for navigation phase");
            }

            // Find the navController field and activate it
            var navField = HarmonyLib.AccessTools.Field(typeof(RNG.Scenarios.DialogueSystemScenario), "navController");
            var navObj = navField?.GetValue(dss) as GameObject;
            if (navObj != null)
            {
                navObj.SetActive(true);
                _log.LogInfo("[ApplyService] Activated TextScenario navController for spectating");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[ApplyService] ActivateClientNavigation failed: {ex.Message}");
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
        scene == "ForestWinScene" || scene == "CastleWinScene" ||
        scene == "FinalWinScene" || scene == "CoreWinScene" ||
        scene == "RunSummary";

    /// <summary>
    /// Returns true if the client is on an interactive scene where it's actively
    /// making choices. Heartbeat scene mismatches should NOT trigger transitions
    /// away from these scenes — the host may simply not have loaded yet.
    /// </summary>
    private static bool IsClientInteractiveScene(string scene) =>
        scene == "ShopScenario" || scene == "Treasure" ||
        scene == "TextScenario" || scene == "PegMinigame";

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
    private string _lastNavOrbApplied;

    private void TriggerNavigationIfNeeded(FullGameStateSnapshot snapshot)
    {
        var battleState = snapshot.Enemies?.BattleStateName;
        bool isNav = battleState == "NAVIGATION" || battleState == "AWAITING_POST_BATTLE_CONTROLLER";

        if (!isNav)
        {
            _navigationTriggered = false;
            _lastNavOrbApplied = null;
            return;
        }

        try
        {
            // One-shot: peg reset + initial slot icon layout. Safe to run once
            // since the icons and peg state don't change mid-navigation.
            if (!_navigationTriggered)
            {
                var navTypes = snapshot.Map?.NavChildNodeTypes;
                if (navTypes != null && navTypes.Count > 0 && (snapshot.Map?.IsNavigating ?? false))
                {
                    _mapApplier.ApplyNavigationSlots(navTypes);
                }

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
                _log.LogInfo($"[ApplyService] Navigation triggered: {navTypes?.Count ?? 0} child nodes");
            }

            // Ball renderer update is not gated by _navigationTriggered: when
            // navigation first enters, snapshot.Deck.CurrentOrb may still be
            // empty (host hasn't drawn NavigationOrb yet), so we need to keep
            // retrying on subsequent heartbeats until the orb actually populates.
            var activeOrb = snapshot.Deck?.CurrentOrb;
            if (!string.IsNullOrEmpty(activeOrb)
                && activeOrb.Contains("NavigationOrb")
                && activeOrb != _lastNavOrbApplied)
            {
                ClientBallRenderer.Instance?.OnOrbDrawn(activeOrb);
                _lastNavOrbApplied = activeOrb;
                _log.LogInfo($"[ApplyService] NavigationOrb sprite applied: {activeOrb}");
            }
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
                    // Use per-player status effects from the summary, not
                    // snapshot.Player (which is the host's active player and may
                    // be a different coop slot with different effects).
                    if (summary.StatusEffects != null && summary.StatusEffects.Count > 0)
                    {
                        myPlayerState.StatusEffects = summary.StatusEffects;
                    }
                    if (snapshot.Player != null)
                    {
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
        try
        {
            action();
            _log.LogInfo($"[ApplyService] {name} OK");
        }
        catch (Exception ex) { _log.LogError($"[ApplyService] {name} failed: {ex.Message}"); }
    }
}
