using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Data;
using HarmonyLib;
using Loading;
using Map;
using Peglin.ClassSystem;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Patches;
using PeglinUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worldmap;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class MapStateApplier : IGameStateApplier<MapStateSnapshot>
{
    private readonly ManualLogSource _log;

    /// <summary>
    /// Message to display on the mirror client when waiting for the host.
    /// Null when no waiting state is active (client should render the game).
    /// </summary>
    public static string ClientWaitingMessage { get; set; }

    private static readonly HashSet<string> MapScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ForestMap", "CastleMap", "MinesMap", "CoreMap"
    };

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
    /// Set when the client loads Battle (via NodeActivated or MapApplier).
    /// While true, stale map syncs (host still on ForestMap) are ignored.
    /// Cleared when the host confirms it's on Battle (hostScene=Battle).
    /// </summary>
    public static bool AwaitingHostBattleConfirmation { get; set; }

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

            // Scenes where the client can't follow — show waiting message instead
            if (snapshot.ActiveScene == "PostMainMenu")
            {
                ClientWaitingMessage = "Host is selecting starting relic...";
                _log.LogInfo("[MapApplier] Host is on PostMainMenu — showing waiting message");
                return;
            }

            if (snapshot.ActiveScene == "MainMenu")
            {
                ClientWaitingMessage = "Waiting for host to start game...";
                _log.LogInfo("[MapApplier] Host is on MainMenu — showing waiting message");
                return;
            }

            // Event/interaction scenes — host is making choices, client waits
            if (snapshot.ActiveScene == "Treasure")
            {
                ClientWaitingMessage = "Host is completing event...";
                _log.LogInfo("[MapApplier] Host is on Treasure — showing waiting message");
                return;
            }

            if (snapshot.ActiveScene == "TextScenario")
            {
                ClientWaitingMessage = "Host is completing event...";
                _log.LogInfo("[MapApplier] Host is on TextScenario — showing waiting message");
                return;
            }

            if (snapshot.ActiveScene == "ShopScenario")
            {
                ClientWaitingMessage = "Host is shopping...";
                _log.LogInfo("[MapApplier] Host is on ShopScenario — showing waiting message");
                return;
            }

            if (snapshot.ActiveScene == "PegMinigame")
            {
                ClientWaitingMessage = "Host is completing event...";
                _log.LogInfo("[MapApplier] Host is on PegMinigame — showing waiting message");
                return;
            }

            // Act completion / win scenes — host clicks continue, client waits
            if (snapshot.ActiveScene == "ForestWinScene" || snapshot.ActiveScene == "CastleWinScene" ||
                snapshot.ActiveScene == "FinalWinScene" || snapshot.ActiveScene == "CoreWinScene")
            {
                ClientWaitingMessage = "Act complete! Waiting for host...";
                _log.LogInfo($"[MapApplier] Host is on win scene '{snapshot.ActiveScene}' — showing waiting message");
                return;
            }

            if (snapshot.ActiveScene == "RunSummary")
            {
                ClientWaitingMessage = "Host is viewing run summary...";
                _log.LogInfo("[MapApplier] Host is on RunSummary — showing waiting message");
                return;
            }

            // Clear waiting state — we're loading a real game scene
            ClientWaitingMessage = null;

            var currentScene = SceneManager.GetActiveScene().name;
            var targetScene = snapshot.ActiveScene;

            if (string.Equals(currentScene, targetScene, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInfo($"[MapApplier] Already on scene '{currentScene}', static data updated.");

                // Host confirmed it's on Battle — clear the awaiting flag
                if (currentScene == "Battle" && AwaitingHostBattleConfirmation)
                {
                    AwaitingHostBattleConfirmation = false;
                    _log.LogInfo("[MapApplier] Host confirmed Battle — cleared AwaitingHostBattleConfirmation");
                }

                // Apply host's map node types to client
                if (MapScenes.Contains(currentScene) && snapshot.Nodes != null && snapshot.Nodes.Count > 0)
                {
                    ApplyMapNodes(snapshot.Nodes);
                }

                // Show post-battle navigation slots on client
                if (currentScene == "Battle" && snapshot.IsNavigating)
                {
                    ApplyNavigationSlots(snapshot.NavChildNodeTypes);
                }
                return;
            }

            // While awaiting host Battle confirmation, ignore stale map syncs.
            // The client loaded Battle (via NodeActivated), but the host hasn't loaded
            // it yet — heartbeats still say ForestMap. Once the host confirms Battle
            // (hostScene=Battle), the flag clears and normal sync resumes.
            if (currentScene == "Battle" && MapScenes.Contains(targetScene) && AwaitingHostBattleConfirmation)
            {
                _log.LogInfo($"[MapApplier] Ignoring stale map sync '{targetScene}' — awaiting host Battle confirmation");
                return;
            }

            // Set flag when loading Battle so stale map syncs are ignored
            if (targetScene == "Battle")
                AwaitingHostBattleConfirmation = true;
            else
                AwaitingHostBattleConfirmation = false;

            _log.LogInfo($"[MapApplier] Scene change: '{currentScene}' -> '{targetScene}', loading...");
            LoadTargetScene(targetScene);
        }
        catch (Exception ex)
        {
            _log.LogError($"[MapApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ApplyStaticGameData(MapStateSnapshot snapshot)
    {
        var seed = snapshot.CurrentSeed ?? "";
        StaticGameData.currentSeed = seed;
        StaticGameData.seedSet = true;
        StaticGameData.totalFloorCount = snapshot.TotalFloorCount;
        StaticGameData.chosenNextNodeIndex = snapshot.ChosenNextNodeIndex;
        StaticGameData.hasReachedBoss = snapshot.HasReachedBoss;

        if (Enum.IsDefined(typeof(Class), snapshot.ChosenClass))
            StaticGameData.chosenClass = (Class)snapshot.ChosenClass;

        // If the host sent a battle name, find and set the correct MapDataBattle
        // so BattleController.Awake loads the right encounter
        if (!string.IsNullOrEmpty(snapshot.BattleDataName))
        {
            var current = StaticGameData.dataToLoad as MapDataBattle;
            if (current == null || current.name != snapshot.BattleDataName)
            {
                var allBattles = Resources.FindObjectsOfTypeAll<MapDataBattle>();
                var match = allBattles.FirstOrDefault(b => b.name == snapshot.BattleDataName);
                if (match != null)
                {
                    StaticGameData.dataToLoad = match;
                    _log.LogInfo($"[MapApplier] Set dataToLoad to '{match.name}' (pegLayout={match.pegLayout?.name})");
                }
                else
                {
                    _log.LogWarning($"[MapApplier] MapDataBattle '{snapshot.BattleDataName}' not found in {allBattles.Length} loaded assets");
                }
            }
        }

        _log.LogInfo($"[MapApplier] StaticGameData: seed={seed}, floor={snapshot.TotalFloorCount}, class={StaticGameData.chosenClass}, node={snapshot.ChosenNextNodeIndex}, battle={snapshot.BattleDataName}");
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

    /// <summary>
    /// Configure the SlotManagers at the bottom of the Battle scene to show
    /// post-battle navigation icons matching the host's available paths.
    /// </summary>
    private void ApplyNavigationSlots(List<int> childNodeTypes)
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

            // Get icon sprites from StaticGameData.currentNode if available
            MapNode[] childNodes = null;
            if (StaticGameData.currentNode?.ChildNodes != null)
                childNodes = StaticGameData.currentNode.ChildNodes;

            int numChildren = childNodeTypes.Count;

            if (numChildren == 1)
            {
                var roomType = (RoomType)childNodeTypes[0];
                var color = MapNode.GetColorForNodeType(roomType);
                var icon = childNodes != null && childNodes.Length > 0 ? childNodes[0]?.activeIcon : null;

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
                var leftIcon = childNodes != null && childNodes.Length > 0 ? childNodes[0]?.activeIcon : null;

                leftSlot.gameObject.SetActive(true);
                if (leftIcon != null)
                    leftSlot.ConfigureForNavigation(leftIcon, leftColor, 0.2f, 0.8f);
                else
                    leftSlot.ConfigureHalfNavigation(leftColor, 0.2f, 0.8f);

                // Right slot = last child
                var rightType = (RoomType)childNodeTypes[numChildren - 1];
                var rightColor = MapNode.GetColorForNodeType(rightType);
                var rightIcon = childNodes != null && childNodes.Length > 0
                    ? childNodes[childNodes.Length - 1]?.activeIcon : null;

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
                    var centerIcon = childNodes != null && childNodes.Length > 1 ? childNodes[1]?.activeIcon : null;

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
    private void ApplyMapNodes(List<MapNodeEntry> hostNodes)
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

            int applied = 0, skipped = 0;
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

                // Set room type from host
                var hostRoomType = (RoomType)hostNode.RoomType;
                clientNode.RoomType = hostRoomType;

                // Set boss index BEFORE icon generation (GenerateIcon reads it for boss sprite)
                if (hostNode.SelectedBossIndex >= 0 && bossIndexField != null)
                {
                    bossIndexField.SetValue(clientNode, hostNode.SelectedBossIndex);
                }

                // Apply room state WITH icon generation — SetActiveState(setIcon: true) handles:
                //   1. Frame color based on state
                //   2. GenerateIcon() based on RoomType (skips GenerateRoomType since RoomType != NONE)
                //   3. Line drawing to children
                // This replaces the old separate GenerateIcon + SetActiveState(setIcon: false) approach
                // which could leave icons stale if GenerateIcon threw silently.
                var hostState = hostNode.RoomState >= 0 ? (RoomState)hostNode.RoomState : RoomState.UPCOMING;
                try
                {
                    clientNode.SetActiveState(hostState, recursive: false, setIcon: true);
                    // Explicit GenerateIcon in case SetActiveState's internal call was blocked
                    if (clientNode.RoomType != RoomType.NONE)
                        clientNode.GenerateIcon();
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[MapApplier] SetActiveState failed for node {hostNode.Index} " +
                        $"(type={hostRoomType}, state={hostState}): {ex.Message}");
                }

                // Verify the type stuck
                if (applied < 3 || clientNode.RoomType != hostRoomType)
                {
                    _log.LogInfo($"[MapApplier] Node {hostNode.Index}: set={hostRoomType}, actual={clientNode.RoomType}, state={hostState}");
                }

                applied++;
            }

            _log.LogInfo($"[MapApplier] Applied {applied} node types from host, {skipped} skipped " +
                $"(host={hostNodes.Count}, client={clientNodes.Length})");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] ApplyMapNodes failed: {ex.Message}");
        }
    }
}
