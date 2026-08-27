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

    /// <summary>
    /// PredictionManager.CopyAllPegs clones the whole pegboard into the
    /// simulation holder and then DeactivateRenderers *destroys* every
    /// MeshRenderer on the clones. LongPegSlimeBehaviour cached its renderer in
    /// Awake, so on a slimed long peg the clone's ApplySlime dereferences a
    /// destroyed renderer:
    ///   Renderer.get_material → ApplySlime → LongPeg.SetActiveStatus
    ///   → LongPeg.SetPegStatus → PredictionManager.UpdateAllPegsStatus
    ///   → PachinkoBall.Arm
    /// Arm() aborts before SetLineRendererStatus(true), so the dotted aimer
    /// never appears for that turn (client symptom: "no aimer on some turns").
    /// Colouring a destroyed renderer is a no-op anyway — skip the call.
    /// </summary>
    [HarmonyPatch(typeof(Battle.PegBehaviour.LongPegSlimeBehaviour), "ApplySlime")]
    [HarmonyPrefix]
    public static bool LongPegSlimeBehaviour_ApplySlime_Prefix(
        UnityEngine.MeshRenderer ____meshRenderer)
    {
        // Unity's == overload reports destroyed objects as null.
        return ____meshRenderer != null;
    }
}
