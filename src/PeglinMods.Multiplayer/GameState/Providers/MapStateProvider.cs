using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worldmap;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class MapStateProvider : IGameStateProvider<MapStateSnapshot>
{
    private readonly ManualLogSource _log;

    private static readonly HashSet<string> MapScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ForestMap", "CastleMap", "MinesMap", "CoreMap"
    };

    public MapStateProvider(ManualLogSource log) => _log = log;

    public MapStateSnapshot Capture()
    {
        try
        {
            var currentScene = SceneManager.GetActiveScene().name;
            var snapshot = new MapStateSnapshot
            {
                CurrentSeed = StaticGameData.currentSeed ?? "",
                TotalFloorCount = StaticGameData.totalFloorCount,
                ChosenClass = (int)StaticGameData.chosenClass,
                ChosenClassName = StaticGameData.chosenClass.ToString(),
                ActiveScene = currentScene,
                ChosenNextNodeIndex = StaticGameData.chosenNextNodeIndex,
                HasReachedBoss = StaticGameData.hasReachedBoss,
                RandomStateJson = MultiplayerClientPatches.CapturedPreMapGenRngState,
                BattleDataName = (StaticGameData.dataToLoad as Data.MapDataBattle)?.name,
                PegLayoutName = (StaticGameData.dataToLoad as Data.MapDataBattle)?.pegLayout?.name,
            };

            // Capture map nodes when on a map scene
            if (MapScenes.Contains(currentScene))
            {
                snapshot.Nodes = CaptureMapNodes();
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"MapStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private List<MapNodeEntry> CaptureMapNodes()
    {
        var nodes = new List<MapNodeEntry>();
        try
        {
            var mc = UnityEngine.Object.FindObjectOfType<Map.MapController>();
            if (mc == null) return nodes;

            // Access _nodes field via reflection
            var nodesField = AccessTools.Field(typeof(Map.MapController), "_nodes");
            if (nodesField == null) return nodes;

            var mapNodes = nodesField.GetValue(mc) as MapNode[];
            if (mapNodes == null) return nodes;

            for (int i = 0; i < mapNodes.Length; i++)
            {
                var node = mapNodes[i];
                if (node == null) continue;

                var roomStatusField = AccessTools.Field(typeof(MapNode), "_roomStatus");
                var roomState = roomStatusField?.GetValue(node);
                var bossIndexField = AccessTools.Field(typeof(MapNode), "_selectedBossIndex");
                var bossIndex = bossIndexField?.GetValue(node);

                nodes.Add(new MapNodeEntry
                {
                    Index = i,
                    PosX = node.transform.position.x,
                    PosY = node.transform.position.y,
                    RoomType = (int)node.RoomType,
                    RoomTypeName = node.RoomType.ToString(),
                    MapDataName = node.MapData?.name,
                    RoomState = roomState != null ? (int)roomState : -1,
                    RoomStateName = roomState?.ToString() ?? "?",
                    SelectedBossIndex = bossIndex != null ? (int)bossIndex : -1,
                });
            }

            _log.LogInfo($"[MapProvider] Captured {nodes.Count} map nodes");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapProvider] CaptureMapNodes failed: {ex.Message}");
        }
        return nodes;
    }
}
