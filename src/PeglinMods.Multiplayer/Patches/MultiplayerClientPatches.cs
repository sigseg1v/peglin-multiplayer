using System;
using Battle;
using Data;
using HarmonyLib;
using Map;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;
using Worldmap;
using Random = UnityEngine.Random;

namespace PeglinMods.Multiplayer.Patches;

[HarmonyPatch]
public static class MultiplayerClientPatches
{
    /// <summary>
    /// UnityEngine.Random.State captured BEFORE MapController generates the map.
    /// MapStateProvider reads this to include in the snapshot sent to clients.
    /// </summary>
    internal static string CapturedPreMapGenRngState;

    /// <summary>
    /// RNG state received from the host, to be restored before client map generation.
    /// </summary>
    internal static string PendingRngStateToRestore;

    /// <summary>
    /// Returns true when the client should NOT run its own game logic.
    /// Only true when actively connected as a spectating client.
    /// </summary>
    private static bool ShouldSuppressClientLogic
    {
        get
        {
            if (MultiplayerPlugin.Services == null) return false;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return false;
            return mode.IsSpectating;
        }
    }

    private static bool IsHosting
    {
        get
        {
            if (MultiplayerPlugin.Services == null) return false;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return false;
            return mode.IsHosting;
        }
    }

    // =========================================================================
    // BLOCK CLIENT GAME LOGIC — client is a dumb renderer
    // =========================================================================

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveRun_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveBase_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static bool GameInit_Start_Prefix() => !ShouldSuppressClientLogic;

    // =========================================================================
    // BLOCK CLIENT AUTO-GENERATION — host controls all content
    // =========================================================================

    /// <summary>
    /// Block enemy spawning on client. BattleController.Awake still calls
    /// EnemyManager.Initialize (which sets up slots) but AddStarterEnemies
    /// is blocked. The host sends enemy data and the applier creates them.
    /// LoadEnemyAssets still runs so the prefab cache is populated.
    /// </summary>
    [HarmonyPatch(typeof(EnemyManager), "AddStarterEnemies")]
    [HarmonyPrefix]
    public static bool EnemyManager_AddStarterEnemies_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked AddStarterEnemies — host will send enemies");
        return false;
    }

    /// <summary>
    /// Block special peg type shuffling on client. The pegboard layout loads
    /// with all pegs as REGULAR. The host sends the correct peg types and
    /// the applier sets them.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleSpecialPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked ShuffleSpecialPegs — host will send peg types");
        return false;
    }

    /// <summary>
    /// Block individual special peg creation on client.
    /// Covers ShuffleCritPegs, CreateRefreshPegs, and direct CreateSpecialPegs calls.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "CreateSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_CreateSpecialPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    // =========================================================================
    // RNG STATE CAPTURE — host saves state before map generation
    // =========================================================================

    [HarmonyPatch(typeof(MapController), "Awake")]
    [HarmonyPrefix]
    public static void MapController_Awake_Prefix()
    {
        if (IsHosting)
        {
            CapturedPreMapGenRngState = SerializeRandomState(Random.state);
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Captured pre-map-gen RNG state");
        }
        else if (ShouldSuppressClientLogic && !string.IsNullOrEmpty(PendingRngStateToRestore))
        {
            var restored = DeserializeRandomState(PendingRngStateToRestore);
            if (restored.HasValue)
            {
                Random.state = restored.Value;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Restored host RNG state before map generation");
            }
            PendingRngStateToRestore = null;
        }
    }

    // =========================================================================
    // NODE ACTIVATION SYNC — host sends battle name when activating a node
    // =========================================================================

    [HarmonyPatch(typeof(MapNode), "ActivateNode")]
    [HarmonyPostfix]
    public static void MapNode_ActivateNode_Postfix(MapNode __instance)
    {
        if (!IsHosting) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry)) return;

        var pos = __instance.transform.position;
        string battleName = (__instance.MapData as MapDataBattle)?.name;
        registry.Dispatch(new NodeActivatedEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            BattleName = battleName,
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1}), battle={battleName}");
    }

    // =========================================================================
    // RNG SERIALIZATION HELPERS
    // =========================================================================

    internal static string SerializeRandomState(Random.State state)
    {
        try
        {
            var t = typeof(Random.State);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object boxed = state;
            int s0 = (int)t.GetField("s0", flags).GetValue(boxed);
            int s1 = (int)t.GetField("s1", flags).GetValue(boxed);
            int s2 = (int)t.GetField("s2", flags).GetValue(boxed);
            int s3 = (int)t.GetField("s3", flags).GetValue(boxed);
            return $"{s0},{s1},{s2},{s3}";
        }
        catch { return null; }
    }

    internal static Random.State? DeserializeRandomState(string s)
    {
        try
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 4) return null;
            Random.InitState(0);
            var state = Random.state;
            var t = typeof(Random.State);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object boxed = state;
            t.GetField("s0", flags).SetValue(boxed, int.Parse(parts[0]));
            t.GetField("s1", flags).SetValue(boxed, int.Parse(parts[1]));
            t.GetField("s2", flags).SetValue(boxed, int.Parse(parts[2]));
            t.GetField("s3", flags).SetValue(boxed, int.Parse(parts[3]));
            return (Random.State)boxed;
        }
        catch { return null; }
    }
}
