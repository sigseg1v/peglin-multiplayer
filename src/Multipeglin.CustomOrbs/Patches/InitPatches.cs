using HarmonyLib;

namespace Multipeglin.CustomOrbs.Patches;

/// <summary>
/// Tries to build/inject the custom orbs every time GameInit.Start runs
/// (i.e. at the start of every run). Idempotent — only runs the first time
/// the source prefabs are reachable, then short-circuits.
/// </summary>
[HarmonyPatch(typeof(GameInit), "Start")]
internal static class GameInitStartPatch
{
    private static void Postfix()
    {
        try
        {
            CustomOrbRegistry.EnsureBuilt();
        }
        catch (System.Exception ex)
        {
            Plugin.Logger?.LogError($"[CustomOrbs] EnsureBuilt failed: {ex}");
        }
    }
}
