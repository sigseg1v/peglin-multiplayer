using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Data;
using HarmonyLib;
using Loading;
using Map;
using Peglin.ClassSystem;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;
using Multipeglin.Patches;
using PeglinUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worldmap;

namespace Multipeglin.GameState.Appliers;

public class MapStateApplier : IGameStateApplier<MapStateSnapshot>
{
    private readonly ManualLogSource _log;

    /// <summary>
    /// Message to display on the mirror client when waiting for the host.
    /// Null when no waiting state is active (client should render the game).
    /// </summary>
    public static string ClientWaitingMessage { get; set; }

    /// <summary>Host player name from latest heartbeat, used for spectator banners.</summary>
    public static string HostPlayerName { get; set; }

    private static readonly HashSet<string> MapScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ForestMap", "CastleMap", "MinesMap", "CoreMap"
    };

    /// <summary>
    /// Cache of MapDataBattle assets keyed by name, populated from each MapController
    /// we see. Each act's MapController holds references to that act's battles in
    /// serialized lists. Once the act's scene unloads, those references go out of
    /// scope and Addressables may unload the assets — leaving the client unable to
    /// resolve battle names when the host transitions MapScene → Battle. Holding
    /// refs here keeps the assets alive across scenes.
    /// </summary>
    private static readonly Dictionary<string, MapDataBattle> _battleCache =
        new Dictionary<string, MapDataBattle>();

    /// <summary>
    /// Per-map-scene snapshot of the last node layout + player position we successfully
    /// applied. Restored in <see cref="ApplyCachedOnAwake"/> (called from MapController.Awake
    /// postfix) so the client's first rendered frame after a scene reload already shows the
    /// correct state — eliminating the "empty cards then camera snap" flash between scene
    /// load and the first heartbeat apply.
    /// </summary>
    private class CachedMapState
    {
        public List<MapNodeEntry> Nodes;
        public float? PlayerPosX;
        public float? PlayerPosY;
    }
    private static readonly Dictionary<string, CachedMapState> _mapStateCache =
        new Dictionary<string, CachedMapState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Look up a MapDataBattle by name from the cross-scene cache.
    /// Used by NodeActivatedClientHandler as a fallback when Resources lookup misses.
    /// </summary>
    public static MapDataBattle TryGetCachedBattle(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        _battleCache.TryGetValue(name, out var b);
        return b;
    }

    // Maps scene names (from SceneManager) back to PeglinSceneLoader.Scene enum values.
    private static readonly Dictionary<string, PeglinSceneLoader.Scene> SceneNameToEnum =
        new Dictionary<string, PeglinSceneLoader.Scene>(StringComparer.OrdinalIgnoreCase)
        {
            { "Battle", PeglinSceneLoader.Scene.BATTLE },
            { "MainMenu", PeglinSceneLoader.Scene.MAIN_MENU },
            { "PostMainMenu", PeglinSceneLoader.Scene.POST_MAIN_MENU },
            { "ForestMap", PeglinSceneLoader.Scene.FOREST_MAP },
            { "ForestWinScene", PeglinSceneLoader.Scene.FOREST_WIN },
            { "CastleMap", PeglinSceneLoader.Scene.CASTLE_MAP },
            { "CastleWinScene", PeglinSceneLoader.Scene.CASTLE_WIN },
            { "MinesMap", PeglinSceneLoader.Scene.MINES_MAP },
            { "FinalWinScene", PeglinSceneLoader.Scene.MINES_WIN },
            { "CoreMap", PeglinSceneLoader.Scene.CORE_MAP },
            { "CoreWinScene", PeglinSceneLoader.Scene.CORE_WIN },
            { "Treasure", PeglinSceneLoader.Scene.TREASURE },
            { "TextScenario", PeglinSceneLoader.Scene.TEXT_SCENARIO },
            { "PegMinigame", PeglinSceneLoader.Scene.PEG_MINIGAME },
            { "ShopScenario", PeglinSceneLoader.Scene.SHOP_SCENARIO },
            { "RunSummary", PeglinSceneLoader.Scene.RUN_SUMMARY },
        };

    // Debounce: prevent loading the same scene multiple times from queued events
    private static string _lastRequestedScene;
    private static float _lastRequestTime;

    /// <summary>
    /// Set to a scene name when the client loads a scene via NodeActivated
    /// before the host has confirmed it. While set, stale map syncs (host
    /// still on the map scene) are ignored. Cleared when the host confirms
    /// it's on the same scene.
    /// </summary>
    public static string AwaitingHostSceneConfirmation { get; set; }

    public MapStateApplier(ManualLogSource log) => _log = log;

    public void Apply(MapStateSnapshot snapshot)
    {
        try
        {
            // Apply static game data fields so the game's systems see the host's run state
            ApplyStaticGameData(snapshot);

            // Sync MapController's internal floor count — drives which node row is active
            if (snapshot.MapFloorCount > 0)
            {
                try
                {
                    var mc = UnityEngine.Object.FindObjectOfType<MapController>();
                    if (mc != null)
                    {
                        var floorField = AccessTools.Field(typeof(MapController), "floorCount");
                        if (floorField != null)
                        {
                            int clientFloor = (int)floorField.GetValue(mc);
                            if (clientFloor != snapshot.MapFloorCount)
                            {
                                floorField.SetValue(mc, snapshot.MapFloorCount);
                                _log.LogInfo($"[MapApplier] Set MapController.floorCount: {clientFloor} → {snapshot.MapFloorCount}");
                            }
                        }
                    }
                }
                catch { }
            }

            // Store the host's pre-map-generation RNG state for later restoration
            if (!string.IsNullOrEmpty(snapshot.RandomStateJson))
                MultiplayerClientPatches.PendingRngStateToRestore = snapshot.RandomStateJson;

            // Sync seededNodeData so TextScenario/Treasure RNG rolls match.
            ApplySeededNodeData(snapshot);

            // Scenes where the client can't follow — show waiting message instead
            if (snapshot.ActiveScene == "PostMainMenu")
            {
                if (UI.LobbyUI.GameStartReceived)
                {
                    // Coop mode: let client follow to PostMainMenu so GameInit runs
                    _log.LogInfo("[MapApplier] Coop mode: allowing client to follow to PostMainMenu");
                    // Fall through to normal scene load logic below
                }
                else
                {
                    ClientWaitingMessage = "Host is selecting starting relic...";
                    _log.LogInfo("[MapApplier] Host is on PostMainMenu — showing waiting message");
                    return;
                }
            }

            if (snapshot.ActiveScene == "MainMenu")
            {
                ClientWaitingMessage = "Waiting for host to start game...";
                _log.LogInfo("[MapApplier] Host is on MainMenu — showing waiting message");
                return;
            }

            // Treasure: client loads the scene for native relic selection — don't block.
            if (snapshot.ActiveScene == "Treasure")
            {
                _log.LogInfo("[MapApplier] Host is on Treasure — client follows for native relic UI");
                // Fall through to normal scene load logic below
            }

            // TextScenario: client loads the scene with native dialogue UI.
            // Show a non-dimming banner at the top so the client knows they're spectating.
            if (snapshot.ActiveScene == "TextScenario")
            {
                ClientWaitingMessage = "Host is completing event...";
                _log.LogInfo("[MapApplier] Host is on TextScenario — client spectating");
                // Fall through to normal scene handling (don't return)
            }

            // ShopScenario: client loads the scene for interactive shopping — don't block.
            if (snapshot.ActiveScene == "ShopScenario")
            {
                _log.LogInfo("[MapApplier] Host is on ShopScenario — client follows for interactive shopping");
                // Fall through to normal scene load logic below
            }

            // PegMinigame: client plays independently — no spectating banner needed.
            if (snapshot.ActiveScene == "PegMinigame")
            {
                _log.LogInfo("[MapApplier] Host is on PegMinigame — client plays independently");
                // Fall through to normal scene load logic below
            }

            // Act completion / win scenes — host clicks continue, client waits
            if (snapshot.ActiveScene == "ForestWinScene" || snapshot.ActiveScene == "CastleWinScene" ||
                snapshot.ActiveScene == "FinalWinScene" || snapshot.ActiveScene == "CoreWinScene")
            {
                ClientWaitingMessage = "Act complete! Waiting for host...";
                _log.LogInfo($"[MapApplier] Host is on win scene '{snapshot.ActiveScene}' — showing waiting message");
                return;
            }

            // Clear waiting state — we're loading a real game scene
            ClientWaitingMessage = null;

            var currentScene = SceneManager.GetActiveScene().name;
            var targetScene = snapshot.ActiveScene;

            if (string.Equals(currentScene, targetScene, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInfo($"[MapApplier] Already on scene '{currentScene}', static data updated.");

                // Host confirmed the scene — clear the awaiting flag
                if (!string.IsNullOrEmpty(AwaitingHostSceneConfirmation) &&
                    string.Equals(currentScene, AwaitingHostSceneConfirmation, StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogInfo($"[MapApplier] Host confirmed '{currentScene}' — cleared AwaitingHostSceneConfirmation");
                    AwaitingHostSceneConfirmation = null;
                }

                // Apply host's map node types to client
                if (MapScenes.Contains(currentScene) && snapshot.Nodes != null && snapshot.Nodes.Count > 0)
                {
                    ApplyMapNodes(snapshot.Nodes, snapshot.PlayerMapPosX, snapshot.PlayerMapPosY);
                }

                // Navigation is now triggered by GameStateApplyService.TriggerNavigationIfNeeded()
                // which calls PostBattleController.StartNavigation() directly.
                return;
            }

            // While awaiting host scene confirmation, ignore stale map syncs.
            // The client loaded a scene (via NodeActivated), but the host hasn't loaded
            // it yet — heartbeats still say ForestMap. Once the host confirms the scene,
            // the flag clears and normal sync resumes.
            if (!string.IsNullOrEmpty(AwaitingHostSceneConfirmation) &&
                string.Equals(currentScene, AwaitingHostSceneConfirmation, StringComparison.OrdinalIgnoreCase) &&
                MapScenes.Contains(targetScene))
            {
                _log.LogInfo($"[MapApplier] Ignoring stale map sync '{targetScene}' — awaiting host '{AwaitingHostSceneConfirmation}' confirmation");
                return;
            }

            _log.LogInfo($"[MapApplier] Scene change: '{currentScene}' -> '{targetScene}', loading...");
            LoadTargetScene(targetScene);
        }
        catch (Exception ex)
        {
            _log.LogError($"[MapApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Rebuild StaticGameData.seededNodeData on the client to mirror the host's
    /// active seeded node. This makes DialogueSystemScenario.Awake and
    /// ChestScenarioController.OpenChest produce the same RNG rolls on both
    /// sides (fixes "waterfall: relic vs fight" divergence and treasure rare-roll
    /// divergence).
    /// </summary>
    private void ApplySeededNodeData(MapStateSnapshot snapshot)
    {
        try
        {
            if (string.IsNullOrEmpty(snapshot.SeededNodeKind))
            {
                // Host has no active seeded node — clear ours to avoid stale state
                StaticGameData.seededNodeData = null;
                return;
            }

            if (snapshot.SeededNodeKind == "text_scenario")
            {
                var node = StaticGameData.seededNodeData as SeededTextScenarioNodeData;
                if (node == null)
                {
                    node = new SeededTextScenarioNodeData();
                    StaticGameData.seededNodeData = node;
                }
                if (snapshot.SeededNodeInitSeed.HasValue && snapshot.SeededNodeTimesUsed.HasValue)
                {
                    // Only rebuild if seed/timesUsed changed — avoid re-iterating the
                    // SerializableRandomState forward iteration every heartbeat.
                    if (node.randomState == null
                        || node.randomState.initializationSeed != snapshot.SeededNodeInitSeed.Value
                        || node.randomState.timesUsed != snapshot.SeededNodeTimesUsed.Value)
                    {
                        node.randomState = new SerializableRandomState(
                            snapshot.SeededNodeInitSeed.Value,
                            snapshot.SeededNodeTimesUsed.Value);
                        _log.LogInfo(
                            $"[MapApplier] Synced SeededTextScenarioNodeData: initSeed={snapshot.SeededNodeInitSeed}, timesUsed={snapshot.SeededNodeTimesUsed}");
                    }
                }
            }
            else if (snapshot.SeededNodeKind == "treasure")
            {
                var node = StaticGameData.seededNodeData as SeededTreasureNodeData;
                if (node == null)
                {
                    node = new SeededTreasureNodeData();
                    StaticGameData.seededNodeData = node;
                }
                if (snapshot.SeededTreasureRareRelicRoll.HasValue)
                    node.rareRelicChanceRoll = snapshot.SeededTreasureRareRelicRoll.Value;
                if (snapshot.SeededTreasureMimicRoll.HasValue)
                    node.mimicChallengeChanceRoll = snapshot.SeededTreasureMimicRoll.Value;
            }
            else if (snapshot.SeededNodeKind == "shop")
            {
                var node = StaticGameData.seededNodeData as SeededShopNodeData;
                if (node == null)
                {
                    node = new SeededShopNodeData();
                    StaticGameData.seededNodeData = node;
                }
                if (snapshot.SeededShopRareRelicRoll.HasValue)
                    node.rareRelicChanceRoll = snapshot.SeededShopRareRelicRoll.Value;
                if (snapshot.SeededShopRelicRoll.HasValue)
                    node.shopRelicChanceRoll = snapshot.SeededShopRelicRoll.Value;

                if (snapshot.SeededShopOrbNames != null && snapshot.SeededShopOrbRarities != null
                    && snapshot.SeededShopOrbNames.Count == snapshot.SeededShopOrbRarities.Count)
                {
                    var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
                    var dm = dms.Length > 0 ? dms[0] : null;
                    if (dm != null)
                    {
                        var orbs = new SeededOrbAndRarity[snapshot.SeededShopOrbNames.Count];
                        for (int i = 0; i < orbs.Length; i++)
                        {
                            var prefab = dm.GetOrbPrefabFromName(snapshot.SeededShopOrbNames[i]);
                            orbs[i] = new SeededOrbAndRarity(
                                prefab, (PachinkoBall.OrbRarity)snapshot.SeededShopOrbRarities[i]);
                        }
                        node.shopOrbs = orbs;
                        _log.LogInfo(
                            $"[MapApplier] Synced SeededShopNodeData: rareRoll={node.rareRelicChanceRoll:F3}, " +
                            $"shopRoll={node.shopRelicChanceRoll:F3}, orbs={orbs.Length}");
                    }
                    else
                    {
                        _log.LogWarning("[MapApplier] DeckManager not found — cannot resolve shop orbs");
                    }
                }

                // Stash the host's chosen shop relic effects so the client's
                // SetUpRelicOffer patch can replicate them instead of running
                // its own RNG-blocked queue logic.
                if (snapshot.SeededShopRelicEffects != null && snapshot.SeededShopRelicEffects.Count > 0)
                {
                    var prev = Multipeglin.Patches.ShopRelicSyncState.LatestRelicEffects;
                    Multipeglin.Patches.ShopRelicSyncState.LatestRelicEffects = snapshot.SeededShopRelicEffects;
                    bool changed = prev == null || prev.Count != snapshot.SeededShopRelicEffects.Count;
                    if (!changed && prev != null)
                    {
                        for (int i = 0; i < prev.Count; i++)
                            if (prev[i] != snapshot.SeededShopRelicEffects[i]) { changed = true; break; }
                    }
                    if (changed)
                    {
                        _log.LogInfo($"[MapApplier] Shop relic effects updated: [{string.Join(",", snapshot.SeededShopRelicEffects)}]");
                        Multipeglin.Patches.ShopRelicSyncState.RefreshDisplayedShopRelics(_log);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] ApplySeededNodeData failed: {ex.Message}");
        }
    }

    private void ApplyStaticGameData(MapStateSnapshot snapshot)
    {
        // Populate the cross-scene battle cache any time we see a MapController.
        // Castle/Mines/Core battles are only held by their scene's MapController,
        // so we must cache them BEFORE the scene unloads for the Battle transition.
        CacheMapBattlesFromController();

        var seed = snapshot.CurrentSeed ?? "";
        StaticGameData.currentSeed = seed;
        StaticGameData.seedSet = true;
        StaticGameData.totalFloorCount = snapshot.TotalFloorCount;
        StaticGameData.chosenNextNodeIndex = snapshot.ChosenNextNodeIndex;
        StaticGameData.hasReachedBoss = snapshot.HasReachedBoss;

        // Do NOT overwrite StaticGameData.chosenClass from the host's snapshot.
        // The client sets their own class in GameStartClientHandler from their
        // lobby selection. The snapshot's ChosenClass reflects whichever player's
        // state is currently hot-swapped into the host's singletons, which
        // would cause the client's player sprite to flicker to the wrong class
        // every heartbeat. Per-slot classes come through CoopPlayerState instead.

        // If the host sent a battle name, find and set the correct MapDataBattle
        // so BattleController.Awake loads the right encounter
        if (!string.IsNullOrEmpty(snapshot.BattleDataName))
        {
            var current = StaticGameData.dataToLoad as MapDataBattle;
            if (current == null || current.name != snapshot.BattleDataName)
            {
                var allBattles = Resources.FindObjectsOfTypeAll<MapDataBattle>();
                var match = allBattles.FirstOrDefault(b => b.name == snapshot.BattleDataName);

                // Cross-scene cache (populated while on map scenes). Castle/Mines/Core
                // battles are only live when their map scene is loaded, so after the
                // Battle transition starts, Resources may no longer include them.
                if (match == null && _battleCache.TryGetValue(snapshot.BattleDataName, out var cached) && cached != null)
                {
                    match = cached;
                    _log.LogInfo($"[MapApplier] Resolved '{match.name}' via cross-scene battle cache");
                }

                // Scenario battles (e.g. RainbowSlimeOnlyScenarioBattle) are loaded via
                // Addressables in the TextScenario scene, which the client skips.
                // Try loading from the Addressable catalog by name.
                if (match == null)
                {
                    match = TryLoadBattleFromAddressables(snapshot.BattleDataName);
                }

                if (match != null)
                {
                    // Prefer MapController's battle-specific arrays (correct frame for the act).
                    // Scenario-path battles (e.g. Waterfall → StompyStompMiniboss) skip
                    // NodeActivated's AssignBattleVisuals, so the battle frame would otherwise
                    // fall back to either the scenario's frame (wrong) or a dummy (empty).
                    Events.Handlers.Map.BattleVisualAssigner.Assign(match, _log, "MapApplier");

                    // Last-resort: copy from current dataToLoad if still missing
                    if (match.background == null && current != null)
                        match.background = current.background;
                    if (match.pegboardFrame == null && current != null)
                        match.pegboardFrame = current.pegboardFrame;

                    StaticGameData.dataToLoad = match;
                    _log.LogInfo($"[MapApplier] Set dataToLoad to '{match.name}' (pegLayout={match.pegLayout?.name}, pegboardFrame={match.pegboardFrame?.name ?? "NULL"}, background={match.background?.name ?? "NULL"})");
                }
                else
                {
                    _log.LogWarning($"[MapApplier] MapDataBattle '{snapshot.BattleDataName}' not found in {allBattles.Length} loaded assets or Addressables");
                }
            }
        }

        _log.LogInfo($"[MapApplier] StaticGameData: seed={seed}, floor={snapshot.TotalFloorCount}, class={StaticGameData.chosenClass}, node={snapshot.ChosenNextNodeIndex}, battle={snapshot.BattleDataName}");
    }

    /// <summary>
    /// Capture every MapDataBattle referenced by the current scene's MapController
    /// into the static cross-scene cache. Each act's MapController holds references
    /// to its act-specific battles in _potentialEasyBattles / _potentialRandomBattles
    /// / _potentialEliteBattles / _mimicBatteData. Caching them here keeps the asset
    /// references alive after the Battle scene load tears down the MapController.
    /// </summary>
    private void CacheMapBattlesFromController()
    {
        try
        {
            var mc = UnityEngine.Object.FindObjectOfType<MapController>();
            if (mc == null) return;

            void Add(System.Collections.IList list)
            {
                if (list == null) return;
                foreach (var obj in list)
                {
                    if (obj is MapDataBattle b && !string.IsNullOrEmpty(b.name))
                        _battleCache[b.name] = b;
                }
            }
            Add(AccessTools.Field(typeof(MapController), "_potentialEasyBattles")?.GetValue(mc) as System.Collections.IList);
            Add(AccessTools.Field(typeof(MapController), "_potentialRandomBattles")?.GetValue(mc) as System.Collections.IList);
            Add(AccessTools.Field(typeof(MapController), "_potentialEliteBattles")?.GetValue(mc) as System.Collections.IList);

            var mimic = AccessTools.Field(typeof(MapController), "_mimicBatteData")?.GetValue(mc) as MapDataBattle;
            if (mimic != null && !string.IsNullOrEmpty(mimic.name))
                _battleCache[mimic.name] = mimic;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] CacheMapBattlesFromController failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Try to load a MapDataBattle from the Addressable catalog by searching all
    /// resource locations for a matching name. This handles scenario battles that
    /// are only loaded when the TextScenario scene runs (which the client skips).
    /// </summary>
    private MapDataBattle TryLoadBattleFromAddressables(string battleName)
    {
        try
        {
            // Search all Addressable locations for MapDataBattle assets
            var locHandle = UnityEngine.AddressableAssets.Addressables
                .LoadResourceLocationsAsync(typeof(MapDataBattle));
            var locations = locHandle.WaitForCompletion();

            if (locations != null)
            {
                foreach (var loc in locations)
                {
                    if (loc.PrimaryKey.Contains(battleName) ||
                        loc.InternalId.Contains(battleName))
                    {
                        var assetHandle = UnityEngine.AddressableAssets.Addressables
                            .LoadAssetAsync<MapDataBattle>(loc);
                        var battle = assetHandle.WaitForCompletion();
                        if (battle != null)
                        {
                            _log.LogInfo($"[MapApplier] Loaded '{battle.name}' via Addressables (key={loc.PrimaryKey})");
                            return battle;
                        }
                    }
                }
                _log.LogInfo($"[MapApplier] Addressables: searched {locations.Count} locations, no match for '{battleName}'");
            }

            // Fallback: try loading by name directly as an Addressable key
            try
            {
                var directHandle = UnityEngine.AddressableAssets.Addressables
                    .LoadAssetAsync<MapDataBattle>(battleName);
                var direct = directHandle.WaitForCompletion();
                if (direct != null)
                {
                    _log.LogInfo($"[MapApplier] Loaded '{direct.name}' via Addressables direct key");
                    return direct;
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] Addressables lookup failed for '{battleName}': {ex.Message}");
        }
        return null;
    }

    private void LoadTargetScene(string targetSceneName)
    {
        // Debounce: don't reload the same scene if we just requested it
        if (targetSceneName == _lastRequestedScene && Time.time - _lastRequestTime < 5f)
        {
            _log.LogInfo($"[MapApplier] Debounced duplicate load: {targetSceneName}");
            return;
        }
        _lastRequestedScene = targetSceneName;
        _lastRequestTime = Time.time;

        // Try to use PeglinSceneLoader.Instance for proper fade/loading screen
        var sceneLoader = PeglinSceneLoader.Instance;
        if (sceneLoader != null && SceneNameToEnum.TryGetValue(targetSceneName, out var sceneEnum))
        {
            _log.LogInfo($"[MapApplier] Using PeglinSceneLoader.LoadScene({sceneEnum})");
            MultiplayerClientPatches.AllowNextSceneLoad = true;
            sceneLoader.LoadScene(sceneEnum);
            return;
        }

        // Fallback: use Unity SceneManager directly
        _log.LogWarning($"[MapApplier] PeglinSceneLoader unavailable or unknown scene '{targetSceneName}', falling back to SceneManager");
        SceneManager.LoadScene(targetSceneName);
    }

    /// <summary>
    /// Track whether we've already configured navigation slots this battle,
    /// so we don't re-tween every heartbeat.
    /// </summary>
    private static bool _navigationSlotsConfigured;
    private static int _lastNavigationHash;

    /// <summary>Reset navigation state when a new battle starts.</summary>
    public static void ResetNavigationState()
    {
        _navigationSlotsConfigured = false;
        _lastNavigationHash = 0;
    }

    /// <summary>Reset all static state on disconnect.</summary>
    public static void ResetAllState()
    {
        ClientWaitingMessage = null;
        AwaitingHostSceneConfirmation = null;
        _lastRequestedScene = null;
        _lastRequestTime = 0;
        _mapStateCache.Clear();
        ResetNavigationState();
    }

    /// <summary>
    /// Apply the last-known good map state to a freshly-awoken MapController on the
    /// client. Runs from <see cref="Patches.MultiplayerClientPatches.MapController_Awake_Postfix"/>
    /// so that node types, node states, and the player's position are all correct
    /// BEFORE the first frame renders and BEFORE <c>Start()</c> captures
    /// <c>_playerInitialPosition</c> (which the <c>PrePanWait</c> camera tween reads).
    /// Without this, the first heartbeat-based apply happens ~50ms after scene load
    /// with stale player data from the host's pre-<c>Start</c> capture, so the user
    /// sees a brief default-position render, then a camera snap when the second
    /// apply arrives with the real host position. Applying from cache here makes
    /// scene reloads visually seamless when the host returns to a previously visited
    /// map scene.
    /// </summary>
    public static void ApplyCachedOnAwake(MapController mc, ManualLogSource log)
    {
        try
        {
            if (mc == null) return;
            var sceneName = mc.gameObject.scene.name;
            if (!_mapStateCache.TryGetValue(sceneName, out var cached) || cached == null) return;

            var nodesField = AccessTools.Field(typeof(MapController), "_nodes");
            var clientNodes = nodesField?.GetValue(mc) as MapNode[];
            if (clientNodes == null || clientNodes.Length == 0) return;

            var roomStatusField = AccessTools.Field(typeof(MapNode), "_roomStatus");
            var bossIndexField = AccessTools.Field(typeof(MapNode), "_selectedBossIndex");

            // Pre-position node types and states so the first rendered frame is correct.
            int nodesApplied = 0;
            foreach (var entry in cached.Nodes)
            {
                if (entry.Index < 0 || entry.Index >= clientNodes.Length) continue;
                var node = clientNodes[entry.Index];
                if (node == null) continue;

                var type = (RoomType)entry.RoomType;
                var state = entry.RoomState >= 0 ? (RoomState)entry.RoomState : RoomState.UPCOMING;
                var currentState = (RoomState)(roomStatusField?.GetValue(node) ?? RoomState.UPCOMING);
                if (node.RoomType == type && currentState == state) continue;

                node.RoomType = type;
                if (entry.SelectedBossIndex >= 0 && bossIndexField != null)
                    bossIndexField.SetValue(node, entry.SelectedBossIndex);

                try
                {
                    node.SetActiveState(state, recursive: false, setIcon: true);
                    if (node.RoomType != RoomType.NONE)
                        node.GenerateIcon();
                }
                catch { }
                nodesApplied++;
            }

            // Pre-position the player and _previousNode so that IntroFade's
            //   _player.transform.position = _previousNode.transform.position
            // doesn't teleport us back to the scene's default spawn, and so that
            // _playerInitialPosition (captured in Awake BEFORE this postfix runs)
            // reflects the cached position for the camera-pan math in PrePanWait.
            if (cached.PlayerPosX.HasValue && cached.PlayerPosY.HasValue)
            {
                var playerField = AccessTools.Field(typeof(MapController), "_player");
                var player = playerField?.GetValue(mc) as GameObject;
                if (player != null)
                {
                    var pos = player.transform.position;
                    var target = new Vector3(cached.PlayerPosX.Value, cached.PlayerPosY.Value, pos.z);
                    player.transform.position = target;

                    var initField = AccessTools.Field(typeof(MapController), "_playerInitialPosition");
                    initField?.SetValue(mc, target);

                    var prevNodeField = AccessTools.Field(typeof(MapController), "_previousNode");
                    MapNode closest = null;
                    float closestDist = float.MaxValue;
                    foreach (var node in clientNodes)
                    {
                        if (node == null) continue;
                        float d = Vector3.SqrMagnitude(node.transform.position - target);
                        if (d < closestDist) { closestDist = d; closest = node; }
                    }
                    if (closest != null) prevNodeField?.SetValue(mc, closest);
                }
            }

            log?.LogInfo($"[MapApplier] Awake: pre-applied {nodesApplied} cached node changes for '{sceneName}' " +
                $"(playerPos={cached.PlayerPosX?.ToString("F1") ?? "?"},{cached.PlayerPosY?.ToString("F1") ?? "?"})");
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[MapApplier] ApplyCachedOnAwake failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Configure the SlotManagers at the bottom of the Battle scene to show
    /// post-battle navigation icons matching the host's available paths.
    /// </summary>
    public void ApplyNavigationSlots(List<int> childNodeTypes)
    {
        if (childNodeTypes == null || childNodeTypes.Count == 0) return;

        // Compute a hash so we only configure once per set of child types
        int hash = childNodeTypes.Count;
        foreach (var t in childNodeTypes) hash = hash * 31 + t;
        if (_navigationSlotsConfigured && hash == _lastNavigationHash) return;

        try
        {
            // Find the PostBattleController — it's on a disabled GameObject,
            // so FindObjectOfType won't work. Use FindObjectsOfTypeAll instead.
            var pbcs = Resources.FindObjectsOfTypeAll<Battle.PostBattleController>();
            var pbc = pbcs.Length > 0 ? pbcs[0] : null;
            if (pbc == null)
            {
                _log.LogWarning("[MapApplier] No PostBattleController in scene for navigation slots");
                return;
            }

            // Get the three SlotManagers via reflection
            var leftField = AccessTools.Field(typeof(Battle.PostBattleController), "_leftSlotManager");
            var centerField = AccessTools.Field(typeof(Battle.PostBattleController), "_centerSlotManager");
            var rightField = AccessTools.Field(typeof(Battle.PostBattleController), "_rightSlotManager");

            var leftSlot = leftField?.GetValue(pbc) as Battle.SlotManager;
            var centerSlot = centerField?.GetValue(pbc) as Battle.SlotManager;
            var rightSlot = rightField?.GetValue(pbc) as Battle.SlotManager;

            if (leftSlot == null || centerSlot == null || rightSlot == null)
            {
                _log.LogWarning("[MapApplier] Could not find SlotManagers");
                return;
            }

            // Try to get icon sprites from any MapNode in memory (they may still
            // be loaded even though currentNode is null on the client)
            Sprite GetIconForRoomType(RoomType rt)
            {
                try
                {
                    // Find any MapNode with matching RoomType
                    var allNodes = Resources.FindObjectsOfTypeAll<MapNode>();
                    foreach (var node in allNodes)
                    {
                        if (node != null && node.RoomType == rt)
                        {
                            var icon = node.activeIcon;
                            if (icon != null) return icon;
                        }
                    }
                }
                catch { }
                return null;
            }

            int numChildren = childNodeTypes.Count;

            if (numChildren == 1)
            {
                var roomType = (RoomType)childNodeTypes[0];
                var color = MapNode.GetColorForNodeType(roomType);
                var icon = GetIconForRoomType(roomType);

                leftSlot.gameObject.SetActive(true);
                leftSlot.ConfigureHalfNavigation(color, 0.2f, 0.8f);

                rightSlot.gameObject.SetActive(true);
                rightSlot.ConfigureHalfNavigation(color, 0.2f, 0.8f);

                centerSlot.gameObject.SetActive(true);
                if (icon != null)
                    centerSlot.ConfigureForNavigation(icon, color, 0.2f, 0.8f);
                else
                    centerSlot.ConfigureHalfNavigation(color, 0.2f, 0.8f);
            }
            else
            {
                // Left slot = first child
                var leftType = (RoomType)childNodeTypes[0];
                var leftColor = MapNode.GetColorForNodeType(leftType);
                var leftIcon = GetIconForRoomType(leftType);

                leftSlot.gameObject.SetActive(true);
                if (leftIcon != null)
                    leftSlot.ConfigureForNavigation(leftIcon, leftColor, 0.2f, 0.8f);
                else
                    leftSlot.ConfigureHalfNavigation(leftColor, 0.2f, 0.8f);

                // Right slot = last child
                var rightType = (RoomType)childNodeTypes[numChildren - 1];
                var rightColor = MapNode.GetColorForNodeType(rightType);
                var rightIcon = GetIconForRoomType(rightType);

                rightSlot.gameObject.SetActive(true);
                if (rightIcon != null)
                    rightSlot.ConfigureForNavigation(rightIcon, rightColor, 0.2f, 0.8f);
                else
                    rightSlot.ConfigureHalfNavigation(rightColor, 0.2f, 0.8f);

                // Center slot = middle child (if 3 nodes) or dud
                centerSlot.gameObject.SetActive(true);
                if (numChildren > 2)
                {
                    var centerType = (RoomType)childNodeTypes[1];
                    var centerColor = MapNode.GetColorForNodeType(centerType);
                    var centerIcon = GetIconForRoomType(centerType);

                    if (centerIcon != null)
                        centerSlot.ConfigureForNavigation(centerIcon, centerColor, 0.2f, 0.8f);
                    else
                        centerSlot.ConfigureHalfNavigation(centerColor, 0.2f, 0.8f);
                }
                else
                {
                    centerSlot.ConfigureDudNavigation();
                }
            }

            _navigationSlotsConfigured = true;
            _lastNavigationHash = hash;
            _log.LogInfo($"[MapApplier] Navigation slots configured: {numChildren} child nodes " +
                $"({string.Join(",", childNodeTypes.ConvertAll(t => ((RoomType)t).ToString()))})");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] ApplyNavigationSlots failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply host's map node types to client nodes. Sets RoomType, boss index,
    /// and room state with icon generation. This is the primary way the client's
    /// map is synced since CreateMapDataLists is blocked on client.
    /// </summary>
    private void ApplyMapNodes(List<MapNodeEntry> hostNodes, float? hostPlayerX = null, float? hostPlayerY = null)
    {
        try
        {
            var mc = UnityEngine.Object.FindObjectOfType<MapController>();
            if (mc == null)
            {
                _log.LogWarning("[MapApplier] No MapController found for node sync");
                return;
            }

            var nodesField = AccessTools.Field(typeof(MapController), "_nodes");
            var clientNodes = nodesField?.GetValue(mc) as MapNode[];
            if (clientNodes == null)
            {
                _log.LogWarning("[MapApplier] Could not access client map nodes");
                return;
            }

            var bossIndexField = AccessTools.Field(typeof(MapNode), "_selectedBossIndex");

            // Cache a reflected accessor for _roomStatus so we can compare without reapplying.
            var roomStatusField = AccessTools.Field(typeof(MapNode), "_roomStatus");

            int applied = 0, skipped = 0, unchanged = 0;
            foreach (var hostNode in hostNodes)
            {
                if (hostNode.Index < 0 || hostNode.Index >= clientNodes.Length)
                {
                    skipped++;
                    continue;
                }

                var clientNode = clientNodes[hostNode.Index];
                if (clientNode == null)
                {
                    skipped++;
                    continue;
                }

                var hostRoomType = (RoomType)hostNode.RoomType;
                var hostState = hostNode.RoomState >= 0 ? (RoomState)hostNode.RoomState : RoomState.UPCOMING;

                // Idempotent check: if type + state already match host, skip the
                // SetActiveState/GenerateIcon work entirely. This is critical for the
                // map-flash fix — heartbeats used to re-run GenerateIcon on every node
                // every 2s, which re-toggled icon SpriteRenderer state and could produce
                // subtle visible jitter during scene-load fade-in.
                RoomState currentState = (RoomState)(roomStatusField?.GetValue(clientNode) ?? RoomState.UPCOMING);
                bool bossIndexOk = hostNode.SelectedBossIndex < 0
                    || bossIndexField == null
                    || (int)bossIndexField.GetValue(clientNode) == hostNode.SelectedBossIndex;
                if (clientNode.RoomType == hostRoomType && currentState == hostState && bossIndexOk)
                {
                    unchanged++;
                    applied++;
                    continue;
                }

                clientNode.RoomType = hostRoomType;

                if (hostNode.SelectedBossIndex >= 0 && bossIndexField != null)
                    bossIndexField.SetValue(clientNode, hostNode.SelectedBossIndex);

                try
                {
                    clientNode.SetActiveState(hostState, recursive: false, setIcon: true);
                    if (clientNode.RoomType != RoomType.NONE)
                        clientNode.GenerateIcon();
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[MapApplier] SetActiveState failed for node {hostNode.Index} " +
                        $"(type={hostRoomType}, state={hostState}): {ex.Message}");
                }

                if (applied < 3 || clientNode.RoomType != hostRoomType)
                {
                    _log.LogInfo($"[MapApplier] Node {hostNode.Index}: set={hostRoomType}, actual={clientNode.RoomType}, state={hostState}");
                }

                applied++;
            }

            _log.LogInfo($"[MapApplier] Applied {applied} node types ({unchanged} unchanged) from host, {skipped} skipped " +
                $"(host={hostNodes.Count}, client={clientNodes.Length})");

            // Stash the last successful apply so MapController.Awake can restore state
            // before the first rendered frame on the next scene load.
            if (applied > 0)
            {
                var sceneName = SceneManager.GetActiveScene().name;
                if (MapScenes.Contains(sceneName))
                {
                    _mapStateCache[sceneName] = new CachedMapState
                    {
                        Nodes = hostNodes,
                        PlayerPosX = hostPlayerX,
                        PlayerPosY = hostPlayerY,
                    };
                }
            }

            // Move the player sprite to the host's position.
            // Use absolute position from host if available, fall back to PREVIOUS node.
            MovePlayerToCurrentNode(mc, clientNodes, hostNodes, hostPlayerX, hostPlayerY);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] ApplyMapNodes failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Move the map player sprite to the host's absolute position.
    /// Falls back to PREVIOUS node position if host position is unavailable.
    /// Also updates MapController._previousNode to the closest node.
    /// </summary>
    private void MovePlayerToCurrentNode(MapController mc, MapNode[] clientNodes,
        List<MapNodeEntry> hostNodes, float? hostPlayerX, float? hostPlayerY)
    {
        try
        {
            var playerField = AccessTools.Field(typeof(MapController), "_player");
            var player = playerField?.GetValue(mc) as GameObject;
            if (player == null) return;

            Vector3 targetPos;

            // Use absolute host position if available (primary — direct sync)
            if (hostPlayerX.HasValue && hostPlayerY.HasValue)
            {
                targetPos = new Vector3(hostPlayerX.Value, hostPlayerY.Value, player.transform.position.z);
            }
            else
            {
                // Fallback: find the PREVIOUS node
                MapNode targetNode = null;
                foreach (var entry in hostNodes)
                {
                    if (entry.RoomState == (int)RoomState.PREVIOUS && entry.Index >= 0 && entry.Index < clientNodes.Length)
                    {
                        targetNode = clientNodes[entry.Index];
                        break;
                    }
                }
                if (targetNode == null) return;
                targetPos = targetNode.transform.position;
            }

            if (Vector3.Distance(player.transform.position, targetPos) > 0.1f)
            {
                player.transform.position = targetPos;
                _log.LogInfo($"[MapApplier] Moved player to ({targetPos.x:F1},{targetPos.y:F1})");
            }

            // Update _previousNode to the closest node so game logic references it
            var prevNodeField = AccessTools.Field(typeof(MapController), "_previousNode");
            MapNode closest = null;
            float closestDist = float.MaxValue;
            foreach (var node in clientNodes)
            {
                if (node == null) continue;
                float d = Vector3.SqrMagnitude(node.transform.position - targetPos);
                if (d < closestDist) { closestDist = d; closest = node; }
            }
            if (closest != null)
                prevNodeField?.SetValue(mc, closest);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] MovePlayerToCurrentNode failed: {ex.Message}");
        }
    }
}
