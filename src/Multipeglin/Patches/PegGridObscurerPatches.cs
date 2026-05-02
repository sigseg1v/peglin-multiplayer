using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

/// <summary>
/// SuperSapper boss minesweeper grid. Reveals are driven entirely by the
/// host snapshot via PegboardStateApplier.SyncObscurerGrid — block any
/// client-side collision-driven reveal so the visual state stays
/// authoritative. The client typically has no live ball physics on the
/// pegboard, but this is a defensive guard.
/// </summary>
[HarmonyPatch]
internal static class PegGridObscurerPatches
{
    [HarmonyPatch(typeof(Battle.PegBehaviour.PegGridObscurer), "OnCollisionEnter2D")]
    [HarmonyPrefix]
    public static bool PegGridObscurer_OnCollisionEnter2D_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }
}
