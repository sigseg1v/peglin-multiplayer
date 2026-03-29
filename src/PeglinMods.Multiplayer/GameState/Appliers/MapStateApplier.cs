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

                // Verify map nodes match host when on a map scene
                if (MapScenes.Contains(currentScene) && snapshot.Nodes != null && snapshot.Nodes.Count > 0)
                {
                    VerifyMapNodes(snapshot.Nodes);
                }
                return;
            }

            // If the client is on a map scene, don't load — node activation handles
            // transitions FROM maps (map → battle, map → treasure, etc.)
            if (MapScenes.Contains(currentScene))
            {
                _log.LogInfo($"[MapApplier] On map '{currentScene}', node activation will handle transition to '{targetScene}'");
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
    /// Compare host map nodes against client map nodes and log mismatches.
    /// This helps diagnose when the client's map doesn't match the host's.
    /// </summary>
    private void VerifyMapNodes(List<MapNodeEntry> hostNodes)
    {
        try
        {
            var mc = UnityEngine.Object.FindObjectOfType<MapController>();
            if (mc == null)
            {
                _log.LogWarning("[MapApplier] No MapController found for node verification");
                return;
            }

            var nodesField = AccessTools.Field(typeof(MapController), "_nodes");
            var clientNodes = nodesField?.GetValue(mc) as MapNode[];
            if (clientNodes == null)
            {
                _log.LogWarning("[MapApplier] Could not access client map nodes");
                return;
            }

            int matches = 0, mismatches = 0;
            foreach (var hostNode in hostNodes)
            {
                if (hostNode.Index < 0 || hostNode.Index >= clientNodes.Length)
                {
                    _log.LogWarning($"[MapApplier] Host node index {hostNode.Index} out of range (client has {clientNodes.Length} nodes)");
                    mismatches++;
                    continue;
                }

                var clientNode = clientNodes[hostNode.Index];
                if (clientNode == null)
                {
                    mismatches++;
                    continue;
                }

                bool typeMatch = (int)clientNode.RoomType == hostNode.RoomType;
                float dx = clientNode.transform.position.x - hostNode.PosX;
                float dy = clientNode.transform.position.y - hostNode.PosY;
                bool posMatch = (dx * dx + dy * dy) < 1f;

                if (typeMatch && posMatch)
                {
                    matches++;
                }
                else
                {
                    _log.LogWarning($"[MapApplier] Node [{hostNode.Index}] MISMATCH: " +
                        $"host={hostNode.RoomTypeName}@({hostNode.PosX:F1},{hostNode.PosY:F1}) data={hostNode.MapDataName}, " +
                        $"client={clientNode.RoomType}@({clientNode.transform.position.x:F1},{clientNode.transform.position.y:F1}) data={clientNode.MapData?.name}");
                    mismatches++;
                }
            }

            _log.LogInfo($"[MapApplier] Map node verification: {matches} match, {mismatches} mismatch " +
                $"(host={hostNodes.Count}, client={clientNodes.Length})");

            if (mismatches > 0 && mismatches > matches / 2)
            {
                _log.LogError($"[MapApplier] SEVERE MAP MISMATCH — {mismatches}/{hostNodes.Count} nodes differ. " +
                    "Map may need regeneration with host's RNG state.");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapApplier] VerifyMapNodes failed: {ex.Message}");
        }
    }
}
