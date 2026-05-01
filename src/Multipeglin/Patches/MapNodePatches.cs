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
        registry.Dispatch(new NodeActivatedEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            BattleName = battleName,
            RngState = SerializeRandomState(Random.state),
            MapDataName = mapDataName,
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1}), battle={battleName}, mapData={mapDataName}");

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
