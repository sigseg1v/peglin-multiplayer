using Battle;
using HarmonyLib;
using Map;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;
using Worldmap;

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

    // --- Battle & save suppression (client doesn't run game logic) ---

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveRun_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveBase_Prefix() => !ShouldSuppressClientLogic;

    // --- Block GameInit.Start on client (skip PostMainMenu entirely) ---

    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static bool GameInit_Start_Prefix() => !ShouldSuppressClientLogic;

    // --- RNG state capture: host saves state BEFORE map generation ---

    [HarmonyPatch(typeof(MapController), "Awake")]
    [HarmonyPrefix]
    public static void MapController_Awake_Prefix()
    {
        if (IsHosting)
        {
            CapturedPreMapGenRngState = SerializeRandomState(Random.state);
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Captured pre-map-gen RNG state");
        }
        else if (ShouldSuppressClientLogic && !string.IsNullOrEmpty(PendingRngStateToRestore))
        {
            // Client: restore the host's RNG state before map generation
            var restored = DeserializeRandomState(PendingRngStateToRestore);
            if (restored.HasValue)
            {
                Random.state = restored.Value;
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Restored host RNG state before map generation");
            }
            PendingRngStateToRestore = null;
        }
    }

    // --- Node activation sync: host sends node position when activated ---

    [HarmonyPatch(typeof(MapNode), "ActivateNode")]
    [HarmonyPostfix]
    public static void MapNode_ActivateNode_Postfix(MapNode __instance)
    {
        if (!IsHosting) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry)) return;

        var pos = __instance.transform.position;
        registry.Dispatch(new NodeActivatedEvent
        {
            PosX = pos.x,
            PosY = pos.y,
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1})");
    }

    // --- RNG state serialization helpers ---

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
        catch
        {
            return null;
        }
    }

    internal static Random.State? DeserializeRandomState(string s)
    {
        try
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 4) return null;

            // Create state via InitState then overwrite fields
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
        catch
        {
            return null;
        }
    }
}
