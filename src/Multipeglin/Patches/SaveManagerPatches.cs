using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class SaveManagerPatches
{
    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveRun_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveBase_Prefix() => !ShouldSuppressClientLogic;

    /// <summary>Block save requests on client.</summary>
    [HarmonyPatch(typeof(SaveManager), "RequestSave")]
    [HarmonyPrefix]
    public static bool SaveManager_RequestSave_Prefix() => !ShouldSuppressClientLogic;
}
