using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class EnemyInfoManagerPatches
{
    /// <summary>
    /// Block upcoming enemy preview generation on client. The host sends the
    /// actual upcoming enemy list and the applier rebuilds the UI from it.
    /// </summary>
    [HarmonyPatch(typeof(Battle.EnemyInfoManager), "Initialize")]
    [HarmonyPrefix]
    public static bool EnemyInfoManager_Initialize_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked EnemyInfoManager.Initialize — host will send upcoming enemies");
        return false;
    }
}
