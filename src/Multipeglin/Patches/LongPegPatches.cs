using HarmonyLib;
using Multipeglin.Utility;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class LongPegPatches
{
    /// <summary>
    /// CLIENT: native HidePeg Object.Destroy(_collider). Soft-hide instead so
    /// heartbeat refresh can ForceAlive the same instance.
    ///
    /// Host mid-battle RemoveIfCleared (former SetActiveStatus postfix) was
    /// removed: DOFade → SetActive(false) → provider IsDestroyed → client
    /// DestroyPeg destroyed colliders permanently (longpeg-heal-failure.md RC6).
    /// SetActiveStatus(false) already applies destroyed materials / collider off;
    /// end-of-battle fade still happens via RemoveClearedPegs.
    /// </summary>
    [HarmonyPatch(typeof(LongPeg), "HidePeg")]
    [HarmonyPrefix]
    public static bool LongPeg_HidePeg_Prefix(LongPeg __instance)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        try
        {
            LongPegVisualHelper.SoftHide(__instance);
        }
        catch
        {
            try
            {
                __instance.gameObject.SetActive(false);
            }
            catch
            {
            }
        }

        return false;
    }
}
