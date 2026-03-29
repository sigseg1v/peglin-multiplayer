using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Patches;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class MapStateProvider : IGameStateProvider<MapStateSnapshot>
{
    private readonly ManualLogSource _log;

    public MapStateProvider(ManualLogSource log) => _log = log;

    public MapStateSnapshot Capture()
    {
        try
        {
            return new MapStateSnapshot
            {
                CurrentSeed = StaticGameData.currentSeed ?? "",
                TotalFloorCount = StaticGameData.totalFloorCount,
                ChosenClass = (int)StaticGameData.chosenClass,
                ChosenClassName = StaticGameData.chosenClass.ToString(),
                ActiveScene = SceneManager.GetActiveScene().name,
                ChosenNextNodeIndex = StaticGameData.chosenNextNodeIndex,
                HasReachedBoss = StaticGameData.hasReachedBoss,
                RandomStateJson = MultiplayerClientPatches.CapturedPreMapGenRngState,
                BattleDataName = (StaticGameData.dataToLoad as Data.MapDataBattle)?.name,
                PegLayoutName = (StaticGameData.dataToLoad as Data.MapDataBattle)?.pegLayout?.name,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning($"MapStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
