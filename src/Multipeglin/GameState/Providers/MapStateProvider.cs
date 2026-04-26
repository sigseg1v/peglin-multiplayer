using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worldmap;

namespace Multipeglin.GameState.Providers;

public class MapStateProvider : IGameStateProvider<MapStateSnapshot>
{
    private readonly ManualLogSource _log;

    private static readonly HashSet<string> MapScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ForestMap", "CastleMap", "MinesMap", "CoreMap"
    };

    public MapStateProvider(ManualLogSource log) => _log = log;

    // Cached initial shop relic offering. _purchasableRelics slots are nulled
    // when the host buys, but the client's shop is independent — both players
    // should see the same initial lineup. Captured once per shop visit and
    // cleared when we leave ShopScenario.
    private static List<int> _cachedShopRelicEffects;
    private static bool _inShopScenario;

    public MapStateSnapshot Capture()
    {
        try
        {
            var currentScene = SceneManager.GetActiveScene().name;

            if (currentScene != "ShopScenario" && _inShopScenario)
            {
                _cachedShopRelicEffects = null;
                _inShopScenario = false;
            }

            var snapshot = new MapStateSnapshot
            {
                CurrentSeed = StaticGameData.currentSeed ?? string.Empty,
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

            // Capture seeded node data so TextScenario/Treasure RNG rolls match on the client.
            // Without this the waterfall "?" event and treasure rareRelicChanceRoll diverge
            // between host and client (host might pick relic path, client might pick fight).
            CaptureSeededNodeData(snapshot);

            // Capture map nodes and MapController's internal floor count
            if (MapScenes.Contains(currentScene))
            {
                snapshot.Nodes = CaptureMapNodes();

                try
                {
                    var mc = UnityEngine.Object.FindObjectOfType<Map.MapController>();
                    if (mc != null)
                    {
                        var floorField = AccessTools.Field(typeof(Map.MapController), "floorCount");
                        if (floorField != null)
                        {
                            snapshot.MapFloorCount = (int)floorField.GetValue(mc);
                        }

                        // Capture player's absolute position on the map
                        var playerField = AccessTools.Field(typeof(Map.MapController), "_player");
                        var player = playerField?.GetValue(mc) as GameObject;
                        if (player != null)
                        {
                            snapshot.PlayerMapPosX = player.transform.position.x;
                            snapshot.PlayerMapPosY = player.transform.position.y;
                        }
                    }
                }
                catch
                {
                }
            }

            // Capture post-battle navigation state (child node choices)
            if (currentScene == "Battle")
            {
                CaptureNavigationState(snapshot);
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"MapStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private void CaptureSeededNodeData(MapStateSnapshot snapshot)
    {
        try
        {
            var seeded = StaticGameData.seededNodeData;
            if (seeded == null)
            {
                return;
            }

            if (seeded is Map.SeededTextScenarioNodeData textNode)
            {
                snapshot.SeededNodeKind = "text_scenario";
                if (textNode.randomState != null)
                {
                    snapshot.SeededNodeInitSeed = textNode.randomState.initializationSeed;
                    snapshot.SeededNodeTimesUsed = textNode.randomState.timesUsed;
                }
            }
            else if (seeded is Map.SeededTreasureNodeData treasureNode)
            {
                snapshot.SeededNodeKind = "treasure";
                snapshot.SeededTreasureRareRelicRoll = treasureNode.rareRelicChanceRoll;
                snapshot.SeededTreasureMimicRoll = treasureNode.mimicChallengeChanceRoll;
            }
            else if (seeded is Map.SeededShopNodeData shopNode)
            {
                snapshot.SeededNodeKind = "shop";
                snapshot.SeededShopRareRelicRoll = shopNode.rareRelicChanceRoll;
                snapshot.SeededShopRelicRoll = shopNode.shopRelicChanceRoll;

                if (shopNode.shopOrbs != null)
                {
                    var names = new System.Collections.Generic.List<string>();
                    var rarities = new System.Collections.Generic.List<int>();
                    foreach (var entry in shopNode.shopOrbs)
                    {
                        names.Add(entry.orb != null ? entry.orb.name : string.Empty);
                        rarities.Add((int)entry.orbRarity);
                    }

                    snapshot.SeededShopOrbNames = names;
                    snapshot.SeededShopOrbRarities = rarities;
                }

                CaptureShopRelicEffects(snapshot);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapProvider] CaptureSeededNodeData failed: {ex.Message}");
        }
    }

    private void CaptureShopRelicEffects(MapStateSnapshot snapshot)
    {
        try
        {
            _inShopScenario = true;

            // Always send the cached initial offering once we have it. The live
            // _purchasableRelics array nulls out slots as the host buys, but the
            // client shops independently and needs to see the original lineup.
            if (_cachedShopRelicEffects != null && _cachedShopRelicEffects.Count > 0)
            {
                snapshot.SeededShopRelicEffects = _cachedShopRelicEffects;
                return;
            }

            var sm = UnityEngine.Object.FindObjectOfType<Scenarios.Shop.ShopManager>();
            if (sm == null)
            {
                return;
            }

            var purchasableField = AccessTools.Field(typeof(Scenarios.Shop.ShopManager), "_purchasableRelics");
            var arr = purchasableField?.GetValue(sm) as System.Array;
            if (arr == null)
            {
                return;
            }

            var relicField = AccessTools.Field(typeof(Scenarios.Shop.PurchasableRelic), "_relic");
            if (relicField == null)
            {
                return;
            }

            var effects = new List<int>();
            var anyNull = false;
            for (var i = 0; i < arr.Length; i++)
            {
                var entry = arr.GetValue(i);
                if (entry is Scenarios.Shop.PurchasableRelic pr)
                {
                    var relic = relicField.GetValue(pr) as Relics.Relic;
                    if (relic != null)
                    {
                        effects.Add((int)relic.effect);
                    }
                    else
                    {
                        anyNull = true;
                    }
                }
                else
                {
                    anyNull = true;
                }
            }

            if (effects.Count > 0)
            {
                snapshot.SeededShopRelicEffects = effects;
                // Only cache once the initial offering is fully populated — if we
                // catch it mid-purchase (unlikely, but safe), wait for a full view.
                if (!anyNull && effects.Count == arr.Length)
                {
                    _cachedShopRelicEffects = effects;
                    _log.LogInfo($"[MapProvider] Cached initial shop relic offering: {effects.Count} relics");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[MapProvider] CaptureShopRelicEffects failed: {ex.Message}");
        }
    }

    // Cache child node data when currentNode is valid — it gets destroyed after scene cleanup
    private static List<int> _cachedNavChildTypes;
    private static bool _wasNavigating;

    /// <summary>Reset cached state on disconnect.</summary>
    public static void ResetCachedState()
    {
        _cachedNavChildTypes = null;
        _wasNavigating = false;
        _cachedShopRelicEffects = null;
        _inShopScenario = false;
    }

    private void CaptureNavigationState(MapStateSnapshot snapshot)
    {
        try
        {
            var stateField = AccessTools.Field(typeof(Battle.BattleController), "_battleState");
            var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
            if (bc == null || stateField == null)
            {
                return;
            }

            var state = (Battle.BattleController.BattleState)stateField.GetValue(bc);
            var isNav = state == Battle.BattleController.BattleState.AWAITING_POST_BATTLE_CONTROLLER
                      || state == Battle.BattleController.BattleState.NAVIGATION;

            if (!isNav)
            {
                // Clear cache when no longer navigating
                if (_wasNavigating)
                {
                    _cachedNavChildTypes = null;
                    _wasNavigating = false;
                }

                return;
            }

            // Try to capture child node data if not cached yet
            if (_cachedNavChildTypes == null && StaticGameData.currentNode != null)
            {
                var children = StaticGameData.currentNode.ChildNodes;
                if (children != null && children.Length > 0)
                {
                    _cachedNavChildTypes = new List<int>();
                    foreach (var child in children)
                    {
                        _cachedNavChildTypes.Add(child != null ? (int)child.RoomType : 0);
                    }

                    _log.LogInfo($"[MapProvider] Cached {_cachedNavChildTypes.Count} nav child types");
                }
            }

            // Send cached data in every heartbeat during navigation
            if (_cachedNavChildTypes != null && _cachedNavChildTypes.Count > 0)
            {
                snapshot.IsNavigating = true;
                snapshot.NavChildNodeTypes = _cachedNavChildTypes;
                _wasNavigating = true;
            }
        }
        catch
        {
        }
    }

    private List<MapNodeEntry> CaptureMapNodes()
    {
        var nodes = new List<MapNodeEntry>();
        try
        {
            var mc = UnityEngine.Object.FindObjectOfType<Map.MapController>();
            if (mc == null)
            {
                return nodes;
            }

            // Access _nodes field via reflection
            var nodesField = AccessTools.Field(typeof(Map.MapController), "_nodes");
            if (nodesField == null)
            {
                return nodes;
            }

            var mapNodes = nodesField.GetValue(mc) as MapNode[];
            if (mapNodes == null)
            {
                return nodes;
            }

            for (var i = 0; i < mapNodes.Length; i++)
            {
                var node = mapNodes[i];
                if (node == null)
                {
                    continue;
                }

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
