using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Loading;
using Peglin.ClassSystem;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class MapStateApplier : IGameStateApplier<MapStateSnapshot>
{
    private readonly ManualLogSource _log;

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

    public MapStateApplier(ManualLogSource log) => _log = log;

    public void Apply(MapStateSnapshot snapshot)
    {
        try
        {
            // Apply static game data fields so the game's systems see the host's run state
            ApplyStaticGameData(snapshot);

            // Check if we need to follow the host to a different scene
            var currentScene = SceneManager.GetActiveScene().name;
            var targetScene = snapshot.ActiveScene;

            if (string.Equals(currentScene, targetScene, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInfo($"[MapApplier] Already on scene '{currentScene}', static data updated.");
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
        StaticGameData.currentSeed = snapshot.CurrentSeed ?? "";
        StaticGameData.totalFloorCount = snapshot.TotalFloorCount;
        StaticGameData.chosenNextNodeIndex = snapshot.ChosenNextNodeIndex;
        StaticGameData.hasReachedBoss = snapshot.HasReachedBoss;

        if (Enum.IsDefined(typeof(Class), snapshot.ChosenClass))
            StaticGameData.chosenClass = (Class)snapshot.ChosenClass;

        _log.LogInfo($"[MapApplier] StaticGameData: seed={snapshot.CurrentSeed}, floor={snapshot.TotalFloorCount}, class={StaticGameData.chosenClass}, node={snapshot.ChosenNextNodeIndex}");
    }

    private void LoadTargetScene(string targetSceneName)
    {
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
