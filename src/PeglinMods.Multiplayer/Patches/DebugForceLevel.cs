using System;
using HarmonyLib;
using Loading;

namespace PeglinMods.Multiplayer.Patches;

/// <summary>
/// When PEGLIN_MULTI_DEBUG_FORCE_LEVEL is set (e.g. "3" or "3-2"),
/// overrides the starting map scene so the game jumps straight to that act.
///   1 = Forest, 2 = Castle, 3 = Mines, 4 = Core
/// The optional second number sets StaticGameData.totalFloorCount.
/// </summary>
[HarmonyPatch]
public static class DebugForceLevel
{
    private static PeglinSceneLoader.Scene? _forcedScene;
    private static int? _forcedFloorCount;
    private static bool _parsed;

    private static void Parse()
    {
        if (_parsed) return;
        _parsed = true;

        var val = Environment.GetEnvironmentVariable("PEGLIN_MULTI_DEBUG_FORCE_LEVEL");
        if (string.IsNullOrEmpty(val)) return;

        var parts = val.Split('-');
        if (!int.TryParse(parts[0], out var act)) return;

        _forcedScene = act switch
        {
            1 => PeglinSceneLoader.Scene.FOREST_MAP,
            2 => PeglinSceneLoader.Scene.CASTLE_MAP,
            3 => PeglinSceneLoader.Scene.MINES_MAP,
            4 => PeglinSceneLoader.Scene.CORE_MAP,
            _ => null,
        };

        if (parts.Length > 1 && int.TryParse(parts[1], out var floor))
            _forcedFloorCount = floor;

        if (_forcedScene != null)
            MultiplayerPlugin.Logger?.LogInfo(
                $"[DebugForceLevel] Forcing start scene to {_forcedScene} (floor={_forcedFloorCount?.ToString() ?? "default"})");
    }

    [HarmonyPatch(typeof(GameInit), "LoadMapScene")]
    [HarmonyPrefix]
    public static void GameInit_LoadMapScene_Prefix(LoadMapData ___LoadData)
    {
        Parse();
        if (_forcedScene == null || ___LoadData == null) return;

        ___LoadData.SceneToLoad = _forcedScene.Value;

        if (_forcedFloorCount.HasValue)
            StaticGameData.totalFloorCount = _forcedFloorCount.Value;
    }
}
