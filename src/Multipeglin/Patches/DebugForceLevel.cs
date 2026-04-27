using System;
using HarmonyLib;
using Loading;

namespace Multipeglin.Patches;

/// <summary>
/// Debug environment variable overrides for testing:
///
/// PEGLIN_MULTI_DEBUG_FORCE_LEVEL (e.g. "3" or "3-2")
///   Overrides the starting map scene so the game jumps straight to that act.
///   1 = Forest, 2 = Castle, 3 = Mines, 4 = Core
///   The optional second number sets StaticGameData.totalFloorCount.
///
/// PEGLIN_SEED (e.g. "12345")
///   Forces a specific game seed for deterministic map/RNG generation.
///   Set StaticGameData.currentSeed before GameInit.Start picks a random one.
/// </summary>
[HarmonyPatch]
public static class DebugForceLevel
{
    private static PeglinSceneLoader.Scene? _forcedScene;
    private static int? _forcedFloorCount;
    private static string _forcedSeed;
    private static bool _parsed;

    private static void Parse()
    {
        if (_parsed)
        {
            return;
        }

        _parsed = true;

        var val = Environment.GetEnvironmentVariable("PEGLIN_MULTI_DEBUG_FORCE_LEVEL");
        if (!string.IsNullOrEmpty(val))
        {
            var parts = val.Split('-');
            if (int.TryParse(parts[0], out var act))
            {
                _forcedScene = act switch
                {
                    1 => PeglinSceneLoader.Scene.FOREST_MAP,
                    2 => PeglinSceneLoader.Scene.CASTLE_MAP,
                    3 => PeglinSceneLoader.Scene.MINES_MAP,
                    4 => PeglinSceneLoader.Scene.CORE_MAP,
                    _ => null,
                };

                if (parts.Length > 1 && int.TryParse(parts[1], out var floor))
                {
                    _forcedFloorCount = floor;
                }

                if (_forcedScene != null)
                {
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[DebugForceLevel] Forcing start scene to {_forcedScene} (floor={_forcedFloorCount?.ToString() ?? "default"})");
                }
            }
        }

        _forcedSeed = Environment.GetEnvironmentVariable("PEGLIN_SEED");
        if (!string.IsNullOrEmpty(_forcedSeed))
        {
            MultiplayerPlugin.Logger?.LogInfo($"[DebugForceLevel] Forcing seed to '{_forcedSeed}'");
        }
    }

    /// <summary>
    /// Set the seed before GameInit.Start generates a random one.
    /// GameInit.Start checks `if (currentSeed == "")` — if we set it first,
    /// it skips random seed generation and uses ours.
    /// </summary>
    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static void GameInit_Start_Prefix()
    {
        Parse();
        if (string.IsNullOrEmpty(_forcedSeed))
        {
            return;
        }

        StaticGameData.currentSeed = _forcedSeed;
        MultiplayerPlugin.Logger?.LogInfo($"[DebugForceLevel] Set StaticGameData.currentSeed = '{_forcedSeed}'");
    }

    [HarmonyPatch(typeof(GameInit), "LoadMapScene")]
    [HarmonyPrefix]
    public static void GameInit_LoadMapScene_Prefix(LoadMapData ___LoadData)
    {
        Parse();
        if (_forcedScene == null || ___LoadData == null)
        {
            return;
        }

        // Continue mode owns the destination scene — overriding it here would
        // jump the host back to act 1 instead of resuming the saved act, which
        // also hard-desyncs every client (their save context is for a different
        // map than the one the host actually loaded).
        if (Continue.ContinueSession.IsActive)
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[DebugForceLevel] Continue is active (saved scene={___LoadData.SceneToLoad}); skipping force-level override");
            return;
        }

        ___LoadData.SceneToLoad = _forcedScene.Value;

        if (_forcedFloorCount.HasValue)
        {
            StaticGameData.totalFloorCount = _forcedFloorCount.Value;
        }
    }
}
