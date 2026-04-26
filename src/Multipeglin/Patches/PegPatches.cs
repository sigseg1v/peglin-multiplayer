using System.Collections;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PegPatches
{
    /// <summary>
    /// Block client-side delayed bomb conversion coroutine. Called via
    /// RegularPeg.StartDelayedBombConversion / LongPeg conversion paths during
    /// relic triggers. Host-driven sync reconverts via the periodic snapshot.
    /// Returns an empty IEnumerator to avoid StartCoroutine(null) crash.
    /// </summary>
    [HarmonyPatch(typeof(Peg), "WaitAndConvertToBomb")]
    [HarmonyPrefix]
    public static bool Peg_WaitAndConvertToBomb_Prefix(ref IEnumerator __result)
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked Peg.WaitAndConvertToBomb — host drives bomb placement");
            __result = EmptyEnumerator();
            return false;
        }

        return true;
    }
}
