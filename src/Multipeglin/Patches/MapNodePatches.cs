using Data;
using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Network.Map;
using Worldmap;
using static Multipeglin.Patches.MultiplayerClientPatches;
using Random = UnityEngine.Random;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class MapNodePatches
{
    // =========================================================================
    // BLOCK CLIENT RANDOMIZATION — prevent game from overwriting synced state
    // =========================================================================

    /// <summary>
    /// Block random map node type generation on client.
    /// MapController.Start() → rootNode.SetActiveState(NEXT) → GenerateRoomType().
    /// Without this, nodes get random types that fight with our synced types.
    /// </summary>
    [HarmonyPatch(typeof(MapNode), "GenerateRoomType")]
    [HarmonyPrefix]
    public static bool MapNode_GenerateRoomType_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Skip icon generation for NONE type nodes on client.
    /// When GenerateRoomType is blocked, nodes stay NONE. GenerateIcon with NONE
    /// would crash on _icons[-1]. Let it through for valid types (our sync sets them).
    /// </summary>
    [HarmonyPatch(typeof(MapNode), "GenerateIcon")]
    [HarmonyPrefix]
    public static bool MapNode_GenerateIcon_Prefix(MapNode __instance)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return __instance.RoomType != RoomType.NONE;
    }

    // =========================================================================
    // NODE ACTIVATION SYNC — host sends battle name when activating a node
    // =========================================================================

    [HarmonyPatch(typeof(MapNode), "ActivateNode")]
    [HarmonyPostfix]
    public static void MapNode_ActivateNode_Postfix(MapNode __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        if (MultiplayerPlugin.Services == null)
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry))
        {
            return;
        }

        var pos = __instance.transform.position;
        var battleName = (__instance.MapData as MapDataBattle)?.name;
        var mapDataName = string.IsNullOrEmpty(battleName) ? __instance.MapData?.name : null;
        var roomStatus = AccessTools.Field(typeof(MapNode), "_roomStatus")?.GetValue(__instance);
        var roomStatusName = roomStatus?.ToString() ?? "?";
        registry.Dispatch(new NodeActivatedEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            BattleName = battleName,
            RngState = SerializeRandomState(Random.state),
            MapDataName = mapDataName,
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1}), battle={battleName}, mapData={mapDataName}, roomStatus={roomStatusName}");

        // Recovery for the act-2 continue softlock: the auto-walked-to node may
        // not be in NEXT state (continue load can leave _roomStatus at TRAVERSED
        // or UPCOMING if SetUpPreviousAndNextNodes mis-derived _previousNode).
        // ActivateNode silently bails with "Room Not Available", NodeSelected
        // never fires, host is stuck on the map. If we have valid MapData and
        // the room isn't NEXT, force the scene transition manually.
        if (roomStatus is Worldmap.RoomState rs
            && rs != Worldmap.RoomState.NEXT
            && __instance.MapData != null)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatches] Host node activate had roomStatus={rs} (expected NEXT). Forcing scene transition to {__instance.MapData?.name}");

            try
            {
                StaticGameData.seededNodeData = __instance.seededNodeData;
                if (Map.MapController.instance != null)
                {
                    AccessTools.Field(typeof(Map.MapController), "_previousNode")?.SetValue(Map.MapController.instance, __instance);
                }

                AccessTools.Method(typeof(Map.MapController), "LoadSceneFromMapData")
                    ?.Invoke(Map.MapController.instance, new object[] { __instance.MapData });
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientPatches] Forced LoadSceneFromMapData failed: {ex}");
            }
        }

        // Auto-save the coop continue file at every node entry. The previous
        // map view's SaveRun has already written Save_<profile>r.data to disk
        // (MapController.OnSceneLoaded → SaveRun), so the bytes ContinueSaver
        // reads reflect the just-finished map state. The MapController-level
        // OnSceneLoaded postfix that used to trigger this was unreliable on
        // cross-act DDoL'd MCs and silently stopped firing — hook the node
        // activation directly so every battle / treasure / shop / event / boss
        // refresh updates the continue file.
        var nodeLabel = !string.IsNullOrEmpty(battleName)
            ? battleName
            : (!string.IsNullOrEmpty(mapDataName) ? mapDataName : "node");
        Continue.ContinueSaver.Save($"node-activated:{nodeLabel}");
    }
}
