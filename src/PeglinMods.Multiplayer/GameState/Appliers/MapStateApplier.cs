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
    public static string ClientWaitingMessage { get; private set; }

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

    public MapStateApplier(ManualLogSource log) => _log = log;

    public void Apply(MapStateSnapshot snapshot)
    {
        try
        {
            // Apply static game data fields so the game's systems see the host's run state
            ApplyStaticGameData(snapshot);

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

            // Clear waiting state — we're loading a real game scene
            ClientWaitingMessage = null;

            var currentScene = SceneManager.GetActiveScene().name;
            var targetScene = snapshot.ActiveScene;

            if (string.Equals(currentScene, targetScene, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInfo($"[MapApplier] Already on scene '{currentScene}', static data updated.");

                // Apply host's map node types to client
                if (MapScenes.Contains(currentScene) && snapshot.Nodes != null && snapshot.Nodes.Count > 0)
                {
                    ApplyMapNodes(snapshot.Nodes);
                }
                return;
            }

            // If client is on a map and host goes to Battle, NodeActivatedClientHandler handles it.
            // For non-battle scenes (Treasure, PegMinigame, TextScenario, ShopScenario),
            // NodeActivatedClientHandler can't handle them (no BattleName), so load directly.
            if (MapScenes.Contains(currentScene) && targetScene == "Battle")
            {
                _log.LogInfo($"[MapApplier] On map '{currentScene}', NodeActivated will handle transition to Battle");
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
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[MapApplier] SetActiveState failed for node {hostNode.Index} " +
                        $"(type={hostRoomType}, state={hostState}): {ex.Message}");
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
