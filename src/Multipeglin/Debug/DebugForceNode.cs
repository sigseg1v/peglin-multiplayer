using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Loading;
using Map;
using ToolBox.Serialization;
using UnityEngine;
using Worldmap;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Debug;

/// <summary>
/// Debug environment variable for skipping to a specific map stage without a continue save.
///
/// MULTIPEGLIN_FORCE_NODE (e.g. "Mines-10" or "Mines-10@FlickeringRelicMinigame")
///   After the host generates the act map, fast-forwards node traversal so the run is
///   positioned on the map ready to enter the requested floor — e.g. Mines-10 leaves
///   floorCount=9 with the Mines-10 node(s) in NEXT state.
///
///   Optional @ hint:
///     - Single name: match MapData on the target node (PegMinigame nodes resolved via
///       GetPegMinigameScenario). Branched maps need a comma-separated path through each
///       fork, or a known seed+floor baked path is used automatically.
///     - Comma-separated names: exact branch path root → target (MapData substring match
///       per hop), e.g. Mines-10@MinesMushroomSlimeEncounterEASY,...,FlickeringRelicMinigame
///
///   When set without MULTIPEGLIN_FORCE_LEVEL, the act prefix selects the map
///   scene automatically (Forest/Castle/Mines/Core).
///
///   Skipped when Continue mode is active. Host-only. Applied once per session on
///   the first map load of the forced act.
/// </summary>
public static class DebugForceNode
{
    private const string EnvVar = "MULTIPEGLIN_FORCE_NODE";

    private static readonly Regex LabelPattern = new Regex(
        @"^(?<act>[A-Za-z]+|\d+)\-(?<floor>\d+)(?:@(?<hint>.+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Baked branch paths from production logs (seed|act|floor → MapData names).</summary>
    private static readonly Dictionary<string, string[]> KnownPaths =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Finding 9 — seed 4228140002, PegMinigame at Mines-10
            ["4228140002|3|10"] = new[]
            {
                "MinesMushroomSlimeEncounterEASY",
                "MinesMirrorSoloEasy",
                "MidasOrbStatue",
                "Gambler",
                "ShopRoom",
                "SlenderlinMinibossMapData",
                "MinesFlickerSpiralBattle",
                "ForestTreasureMapData",
                "VampireScenarioMapData",
                "FlickeringRelicMinigame",
            },
            // Finding 8 — seed 3081037685, Slenderlin at Mines-7 (after MirrorTunnel ?)
            ["3081037685|3|7"] = new[]
            {
                "MinesEldritchEasyEncounter1",
                "Gambler",
                "MinesFlickerGridBattleEasy",
                "MinesMirrorAndRanged",
                "GhostMinibossMapData",
                "MirrorTunnel",
                "SlenderlinMinibossMapData",
            },
        };

    private static bool _parsed;
    private static bool _applied;
    private static ForceNodeRequest _request;

    public sealed class ForceNodeRequest
    {
        public string ActName;
        public int ActNumber;
        public int TargetFloor;
        public string MapDataHint;
        public PeglinSceneLoader.Scene Scene;
    }

    /// <summary>Non-null when <see cref="EnvVar"/> is set and parsed successfully.</summary>
    public static ForceNodeRequest Request
    {
        get
        {
            Parse();
            return _request;
        }
    }

    public static void Parse()
    {
        if (_parsed)
        {
            return;
        }

        _parsed = true;

        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var match = LabelPattern.Match(raw.Trim());
        if (!match.Success)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[DebugForceNode] Could not parse '{raw}' — expected Act-Floor[@MapDataHint] (e.g. Mines-10@FlickeringRelicMinigame)");
            return;
        }

        if (!int.TryParse(match.Groups["floor"].Value, out var floor) || floor < 1)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[DebugForceNode] Invalid floor in '{raw}'");
            return;
        }

        var actToken = match.Groups["act"].Value;
        if (!TryResolveAct(actToken, out var actName, out var actNumber, out var scene))
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[DebugForceNode] Unknown act '{actToken}' in '{raw}' (use Forest/Castle/Mines/Core or 1-4)");
            return;
        }

        _request = new ForceNodeRequest
        {
            ActName = actName,
            ActNumber = actNumber,
            TargetFloor = floor,
            MapDataHint = match.Groups["hint"].Success ? match.Groups["hint"].Value.Trim() : null,
            Scene = scene,
        };

        MultiplayerPlugin.Logger?.LogInfo(
            $"[DebugForceNode] Will force {actName}-{floor}" +
            (_request.MapDataHint != null ? $" targeting '{_request.MapDataHint}'" : string.Empty));
    }

    /// <summary>Called from <see cref="Patches.DebugForceLevel"/> when level override is absent.</summary>
    public static PeglinSceneLoader.Scene? GetForcedSceneIfNeeded()
    {
        Parse();
        return _request?.Scene;
    }

    /// <summary>Host-only: reposition the freshly generated map to the requested stage.</summary>
    public static void TryApplyAfterMapGen(MapController mc)
    {
        Parse();
        if (_request == null || _applied || mc == null)
        {
            return;
        }

        if (Continue.ContinueSession.IsActive)
        {
            MultiplayerPlugin.Logger?.LogInfo("[DebugForceNode] Continue is active — skipping force-node");
            return;
        }

        if (!IsHosting)
        {
            return;
        }

        var firstLoad = true;
        try
        {
            firstLoad = (bool)(AccessTools.Field(typeof(MapController), "_firstLoad")?.GetValue(mc) ?? true);
        }
        catch
        {
        }

        if (!firstLoad)
        {
            return;
        }

        if (mc.Act != _request.ActNumber)
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[DebugForceNode] Map act={mc.Act} != requested act={_request.ActNumber} — skipping");
            return;
        }

        try
        {
            Apply(mc, _request);
            _applied = true;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[DebugForceNode] Apply failed: {ex}");
        }
    }

    private static void Apply(MapController mc, ForceNodeRequest req)
    {
        var root = mc.rootNode;
        if (root == null)
        {
            MultiplayerPlugin.Logger?.LogWarning("[DebugForceNode] rootNode is null — cannot advance");
            return;
        }

        var path = BuildPath(mc, root, req);
        if (path == null || path.Count < 1)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[DebugForceNode] Could not build path for {req.ActName}-{req.TargetFloor}" +
                (req.MapDataHint != null ? $" (@{req.MapDataHint})" : string.Empty));
            return;
        }

        if (path.Count != req.TargetFloor)
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[DebugForceNode] Path depth {path.Count} differs from label {req.ActName}-{req.TargetFloor} " +
                "(expected when act-skipping — using actual map path)");
        }

        var targetNode = path[path.Count - 1];
        var traversedCount = path.Count - 1;
        for (var i = 0; i < traversedCount; i++)
        {
            MaterializeMapData(mc, path[i]);
            SetNodeState(path[i], RoomState.TRAVERSED);
        }

        MaterializeMapData(mc, targetNode);
        SetNodeState(targetNode, RoomState.NEXT);

        var floorField = AccessTools.Field(typeof(MapController), "floorCount");
        floorField?.SetValue(mc, traversedCount);

        var prevNodeField = AccessTools.Field(typeof(MapController), "_previousNode");
        var previous = traversedCount > 0 ? path[traversedCount - 1] : null;
        prevNodeField?.SetValue(mc, previous);

        try
        {
            AccessTools.Method(typeof(MapController), "SetUpPreviousAndNextNodes")?.Invoke(mc, null);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[DebugForceNode] SetUpPreviousAndNextNodes failed: {ex.Message}");
        }

        PositionPlayer(mc, previous ?? targetNode);

        StaticGameData.totalFloorCount = BaseTotalFloorForAct(req.ActNumber) + traversedCount;

        FlushNodeSave(mc);

        var targetName = ResolveMapDataName(mc, targetNode) ?? targetNode.gameObject.name;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[DebugForceNode] Positioned at {req.ActName}-{req.TargetFloor}: floorCount={traversedCount}, " +
            $"totalFloorCount={StaticGameData.totalFloorCount}, next={targetName}, path={string.Join(" → ", path.Select(n => NodeLabel(mc, n)))}");
    }

    private static List<MapNode> BuildPath(MapController mc, MapNode root, ForceNodeRequest req)
    {
        // Explicit comma-separated branch path (user override).
        if (!string.IsNullOrEmpty(req.MapDataHint) && req.MapDataHint.Contains(","))
        {
            var explicitPath = SplitWaypoints(req.MapDataHint);
            var byExplicit = FindPathByWaypointsDfs(mc, root, explicitPath);
            if (byExplicit != null)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[DebugForceNode] Using explicit waypoint path ({byExplicit.Count} hops)");
                return byExplicit;
            }

            MultiplayerPlugin.Logger?.LogWarning("[DebugForceNode] Explicit waypoint path failed");
        }

        // Primary: locate target node anywhere on the map (act-skip changes root/layout vs prod).
        var searchHint = ResolveSearchHint(req);
        var target = FindTargetNodeOnMap(mc, root, searchHint)
            ?? FindSolePegMinigameNode(mc, root);
        if (target != null)
        {
            var path = FindPath(root, target);
            if (path != null)
            {
                var label = ResolveMapDataName(mc, target) ?? target.gameObject.name;
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[DebugForceNode] Found target '{label}' at path depth {path.Count} (hint='{searchHint}')");
                return path;
            }
        }

        MultiplayerPlugin.Logger?.LogWarning(
            $"[DebugForceNode] No node matched hint '{searchHint}' — cannot skip to repro point");
        return null;
    }

    /// <summary>Hint used to locate the target node on the current map (may differ from prod path when act-skipping).</summary>
    private static string ResolveSearchHint(ForceNodeRequest req)
    {
        if (!string.IsNullOrEmpty(req.MapDataHint) && !req.MapDataHint.Contains(","))
        {
            return req.MapDataHint;
        }

        var known = ResolveKnownWaypoints(req);
        if (known != null && known.Length > 0)
        {
            return known[known.Length - 1];
        }

        return null;
    }

    private static string[] ResolveKnownWaypoints(ForceNodeRequest req)
    {
        var seed = StaticGameData.currentSeed ?? string.Empty;
        var key = $"{seed}|{req.ActNumber}|{req.TargetFloor}";
        if (KnownPaths.TryGetValue(key, out var path))
        {
            return path;
        }

        return null;
    }

    private static string[] SplitWaypoints(string hint) =>
        hint.Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>DFS from root for a child chain matching each waypoint in order (root may differ from prod).</summary>
    private static List<MapNode> FindPathByWaypointsDfs(MapController mc, MapNode root, string[] waypoints)
    {
        if (waypoints.Length == 0)
        {
            return null;
        }

        var found = new List<MapNode>();
        if (DfsWaypointChain(mc, root, waypoints, 0, new List<MapNode>(), found))
        {
            return found;
        }

        return null;
    }

    private static bool DfsWaypointChain(
        MapController mc,
        MapNode node,
        string[] waypoints,
        int waypointIndex,
        List<MapNode> path,
        List<MapNode> result)
    {
        if (node == null)
        {
            return false;
        }

        path.Add(node);

        if (NodeMatchesHint(mc, node, waypoints[waypointIndex]))
        {
            if (waypointIndex == waypoints.Length - 1)
            {
                result.Clear();
                result.AddRange(path);
                return true;
            }

            foreach (var child in GetChildren(node))
            {
                if (DfsWaypointChain(mc, child, waypoints, waypointIndex + 1, path, result))
                {
                    return true;
                }
            }
        }
        else if (waypointIndex == 0)
        {
            // Root often differs when act-skipping — still walk from root but start matching at a child.
            foreach (var child in GetChildren(node))
            {
                if (DfsWaypointChain(mc, child, waypoints, 0, path, result))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static bool NodeMatchesHint(MapController mc, MapNode node, string hint)
    {
        if (node == null || string.IsNullOrEmpty(hint))
        {
            return false;
        }

        var name = ResolveMapDataName(mc, node);
        if (!string.IsNullOrEmpty(name))
        {
            return name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return node.gameObject.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Find a target node anywhere on the map — not limited to act-floor depth.</summary>
    private static MapNode FindTargetNodeOnMap(MapController mc, MapNode root, string hint)
    {
        if (string.IsNullOrEmpty(hint))
        {
            return FindSolePegMinigameNode(mc, root);
        }

        MapNode best = null;
        foreach (var node in EnumerateAllNodes(mc, root))
        {
            if (!NodeMatchesHint(mc, node, hint))
            {
                continue;
            }

            // Prefer peg minigame nodes when hint looks like a minigame target.
            if (node.RoomType == RoomType.PEG_MINIGAME)
            {
                return node;
            }

            best = node;
        }

        if (best != null)
        {
            return best;
        }

        // Hint may only resolve after GetPegMinigameScenario — scan PEG_MINIGAME types explicitly.
        if (hint.IndexOf("minigame", StringComparison.OrdinalIgnoreCase) >= 0
            || hint.IndexOf("FlickeringRelic", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return FindSolePegMinigameNode(mc, root);
        }

        return null;
    }

    private static MapNode FindSolePegMinigameNode(MapController mc, MapNode root)
    {
        MapNode found = null;
        var count = 0;
        foreach (var node in EnumerateAllNodes(mc, root))
        {
            if (node.RoomType != RoomType.PEG_MINIGAME)
            {
                continue;
            }

            count++;
            found = node;
        }

        if (count == 1)
        {
            var name = ResolveMapDataName(mc, found) ?? found.gameObject.name;
            MultiplayerPlugin.Logger?.LogInfo($"[DebugForceNode] Using sole PEG_MINIGAME node: {name}");
            return found;
        }

        if (count > 1)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[DebugForceNode] Map has {count} PEG_MINIGAME nodes — need a specific @hint");
        }

        return null;
    }

    private static IEnumerable<MapNode> EnumerateAllNodes(MapController mc, MapNode root)
    {
        var nodesField = AccessTools.Field(typeof(MapController), "_nodes");
        if (nodesField?.GetValue(mc) is MapNode[] mapNodes && mapNodes.Length > 0)
        {
            foreach (var node in mapNodes)
            {
                if (node != null)
                {
                    yield return node;
                }
            }

            yield break;
        }

        foreach (var node in EnumerateTree(root))
        {
            yield return node;
        }
    }

    private static IEnumerable<MapNode> EnumerateTree(MapNode node)
    {
        if (node == null)
        {
            yield break;
        }

        yield return node;
        foreach (var child in GetChildren(node))
        {
            foreach (var desc in EnumerateTree(child))
            {
                yield return desc;
            }
        }
    }

    private static List<MapNode> FindPath(MapNode root, MapNode target)
    {
        var queue = new Queue<(MapNode node, List<MapNode> path)>();
        queue.Enqueue((root, new List<MapNode> { root }));

        while (queue.Count > 0)
        {
            var (node, path) = queue.Dequeue();
            if (node == target)
            {
                return path;
            }

            foreach (var child in GetChildren(node).OrderBy(c => c.transform.position.x))
            {
                if (path.Contains(child))
                {
                    continue;
                }

                var nextPath = new List<MapNode>(path) { child };
                queue.Enqueue((child, nextPath));
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve the MapData name without calling GenerateRandomMapData (which would reroll the map).
    /// PegMinigame nodes need GetPegMinigameScenario before MapData is populated.
    /// </summary>
    private static string ResolveMapDataName(MapController mc, MapNode node)
    {
        if (node == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(node.MapData?.name))
        {
            return node.MapData.name;
        }

        if (node.RoomType == RoomType.PEG_MINIGAME && mc != null)
        {
            try
            {
                var result = AccessTools.Method(typeof(MapController), "GetPegMinigameScenario")
                    ?.Invoke(mc, new object[] { node });
                if (result != null)
                {
                    var mapData = result.GetType().GetField("Item1")?.GetValue(result) as MapData;
                    if (!string.IsNullOrEmpty(mapData?.name))
                    {
                        return mapData.name;
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    /// <summary>Assign MapData on path nodes so SaveNode / activation see the correct scenario.</summary>
    private static void MaterializeMapData(MapController mc, MapNode node)
    {
        if (node == null || node.MapData != null)
        {
            return;
        }

        if (node.RoomType == RoomType.PEG_MINIGAME && mc != null)
        {
            try
            {
                var result = AccessTools.Method(typeof(MapController), "GetPegMinigameScenario")
                    ?.Invoke(mc, new object[] { node });
                if (result != null)
                {
                    var mapData = result.GetType().GetField("Item1")?.GetValue(result) as MapData;
                    if (mapData != null)
                    {
                        node.MapData = mapData;
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            mc?.GenerateRandomMapData(node);
        }
        catch
        {
        }
    }

    private static string NodeLabel(MapController mc, MapNode node)
    {
        if (node == null)
        {
            return "(null)";
        }

        return ResolveMapDataName(mc, node) ?? node.gameObject.name;
    }

    private static IEnumerable<MapNode> GetChildren(MapNode node)
    {
        if (node == null)
        {
            yield break;
        }

        var childField = AccessTools.Field(typeof(MapNode), "_childNodes");
        if (childField?.GetValue(node) is not MapNode[] children || children.Length == 0)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child != null)
            {
                yield return child;
            }
        }
    }

    private static void SetNodeState(MapNode node, RoomState state)
    {
        try
        {
            node.SetActiveState(state, recursive: false, setIcon: true);
            if (node.RoomType != RoomType.NONE)
            {
                node.GenerateIcon();
            }
        }
        catch
        {
        }
    }

    private static void PositionPlayer(MapController mc, MapNode anchor)
    {
        if (anchor == null)
        {
            return;
        }

        var playerField = AccessTools.Field(typeof(MapController), "_player");
        var player = playerField?.GetValue(mc) as GameObject;
        if (player == null)
        {
            return;
        }

        var targetPos = anchor.transform.position;
        var pos = player.transform.position;
        var snapped = new Vector3(targetPos.x, targetPos.y, pos.z);
        player.transform.position = snapped;

        var initField = AccessTools.Field(typeof(MapController), "_playerInitialPosition");
        initField?.SetValue(mc, snapped);
    }

    private static void FlushNodeSave(MapController mc)
    {
        try
        {
            var nodesField = AccessTools.Field(typeof(MapController), "_nodes");
            if (nodesField?.GetValue(mc) is MapNode[] mapNodes)
            {
                foreach (var mn in mapNodes)
                {
                    mn?.SaveNode();
                }
            }

            DataSerializer.SaveFile(DataSerializer.SaveType.RUN);

            var firstLoadField = AccessTools.Field(typeof(MapController), "_firstLoad");
            firstLoadField?.SetValue(mc, false);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[DebugForceNode] FlushNodeSave failed: {ex.Message}");
        }
    }

    private static int BaseTotalFloorForAct(int act) =>
        act switch
        {
            1 => 0,
            2 => 14,
            3 => 26,
            4 => 38,
            _ => 0,
        };

    private static bool TryResolveAct(
        string token,
        out string actName,
        out int actNumber,
        out PeglinSceneLoader.Scene scene)
    {
        actName = null;
        actNumber = 0;
        scene = default;

        if (int.TryParse(token, out var numeric) && numeric is >= 1 and <= 4)
        {
            actNumber = numeric;
        }
        else
        {
            actName = token.Trim();
            actNumber = actName.ToLowerInvariant() switch
            {
                "forest" => 1,
                "castle" => 2,
                "mines" => 3,
                "core" => 4,
                _ => 0,
            };

            if (actNumber == 0)
            {
                return false;
            }
        }

        actName ??= actNumber switch
        {
            1 => "Forest",
            2 => "Castle",
            3 => "Mines",
            4 => "Core",
            _ => $"Act-{actNumber}",
        };

        scene = actNumber switch
        {
            1 => PeglinSceneLoader.Scene.FOREST_MAP,
            2 => PeglinSceneLoader.Scene.CASTLE_MAP,
            3 => PeglinSceneLoader.Scene.MINES_MAP,
            4 => PeglinSceneLoader.Scene.CORE_MAP,
            _ => default,
        };

        return actNumber is >= 1 and <= 4;
    }
}
