using System.Collections;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class SpiritOfRadiaBossPatches
{
    /// <summary>
    /// Block boss's StartPhase2Transition IEnumerator on the client. The frame
    /// manager's Phase2PreTransition coroutine (which runs on the client to drive
    /// the wall-crack and floor-hide visuals) ends by calling this — but on the
    /// client it must not run host state changes (currentPhase, peg conversion,
    /// status effects, pachinko spawn). The visual delegate is still fired
    /// separately by Step=2 from the host.
    /// </summary>
    [HarmonyPatch(typeof(global::Battle.Enemies.SpiritOfRadiaBoss), nameof(global::Battle.Enemies.SpiritOfRadiaBoss.StartPhase2Transition))]
    [HarmonyPrefix]
    public static bool SpiritOfRadiaBoss_StartPhase2Transition_Prefix(ref IEnumerator __result)
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked SpiritOfRadiaBoss.StartPhase2Transition — host drives phase-2 state");
            __result = EmptyEnumerator();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Symmetric block for boss's StartPhase2PreTransition IEnumerator on the
    /// client. The AI action that calls it is host-only, so this is defensive
    /// in case any path reaches it (e.g. relic effects).
    /// </summary>
    [HarmonyPatch(typeof(global::Battle.Enemies.SpiritOfRadiaBoss), nameof(global::Battle.Enemies.SpiritOfRadiaBoss.StartPhase2PreTransition))]
    [HarmonyPrefix]
    public static bool SpiritOfRadiaBoss_StartPhase2PreTransition_Prefix(ref IEnumerator __result)
    {
        if (ShouldSuppressClientLogic)
        {
            __result = EmptyEnumerator();
            return false;
        }

        return true;
    }
}
