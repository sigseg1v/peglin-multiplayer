using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class SlimeBossPatches
{
    // --- SlimeBoss: block client-side pachinkoBallSpawnLocation mutation ---
    // SlimeBoss.UpdatePachinkoPos() alternates the ball spawn location each turn
    // by incrementing a local counter. On the client this runs independently of
    // the host, so the two sides drift out of sync (host spawns left, client spawns
    // right, etc.). The heartbeat EnemyStateSnapshot carries the host's authoritative
    // pachinkoBallSpawnLocation — block the client-side mutation so only the
    // heartbeat applier writes to it.

    [HarmonyPatch(typeof(Battle.SlimeBoss), "UpdatePachinkoPos")]
    [HarmonyPrefix]
    public static bool SlimeBoss_UpdatePachinkoPos_Prefix()
        => !ShouldSuppressClientLogic;
}
