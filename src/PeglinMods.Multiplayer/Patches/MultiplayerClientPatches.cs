using Battle;
using HarmonyLib;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Patches;

[HarmonyPatch]
public static class MultiplayerClientPatches
{
    /// <summary>
    /// Returns true when the client should NOT run its own game logic.
    /// This applies in spectating mode AND mirror mode (when not hosting).
    /// </summary>
    private static bool ShouldSuppressClientLogic
    {
        get
        {
            if (MultiplayerPlugin.Services == null) return false;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return false;
            return mode.IsSpectating || (mode.ClientMode == ClientMode.Mirror && !mode.IsHosting);
        }
    }

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
}
