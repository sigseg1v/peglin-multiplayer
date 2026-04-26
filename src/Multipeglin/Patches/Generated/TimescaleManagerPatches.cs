using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class TimescaleManagerPatches
{
    // =========================================================================
    // BLOCK CLIENT GAME LOGIC — client is a dumb renderer
    // =========================================================================

    /// <summary>
    /// Block fast forward input on client — host controls game speed.
    /// The host's speedup state is synced via PlayerStateSnapshot.
    /// </summary>
    [HarmonyPatch(typeof(TimescaleManager), "Update")]
    [HarmonyPrefix]
    public static bool TimescaleManager_Update_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(TimescaleManager), "ManualSpeedupToggle")]
    [HarmonyPrefix]
    public static bool TimescaleManager_ManualSpeedupToggle_Prefix() => !ShouldSuppressClientLogic;
}
