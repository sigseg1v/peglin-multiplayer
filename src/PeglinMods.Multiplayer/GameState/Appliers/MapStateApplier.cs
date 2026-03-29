using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Data;
using Loading;
using Peglin.ClassSystem;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Patches;
using PeglinUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            sceneLoader.LoadScene(sceneEnum);
            return;
        }

        // Fallback: use Unity SceneManager directly
        _log.LogWarning($"[MapApplier] PeglinSceneLoader unavailable or unknown scene '{targetSceneName}', falling back to SceneManager");
        SceneManager.LoadScene(targetSceneName);
    }
}
