using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class EnemyManagerPatches
{
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
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked AddStarterEnemies — host will send enemies");
        return false;
    }
}
