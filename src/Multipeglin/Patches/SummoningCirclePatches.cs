using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

/// <summary>
/// Coop fix for SummoningCirclePachinkoBall softlock. SC clones an orb from
/// deckManager.shuffledDeck (parented to battleDeckOrbContainerInstance, which
/// can be inactive); Instantiate preserves activeSelf so satellites spawn
/// inactive, never tick, never fire OnPachinkoBallDestroyed, and the BC
/// counter hangs forever. Postfix forces the satellite active. Finalizer
/// swallows any exception so a single broken prefab can't kill the whole
/// FireOrbs coroutine.
/// </summary>
[HarmonyPatch]
internal static class SummoningCirclePatches
{
    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyPostfix]
    public static void PachinkoBall_SpawnMultiballFromLocation_Postfix(
        PachinkoBall __instance,
        UnityEngine.GameObject __result)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance is not SummoningCirclePachinkoBall || __result == null)
        {
            return;
        }

        if (!__result.activeSelf)
        {
            __result.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyFinalizer]
    public static System.Exception PachinkoBall_SpawnMultiballFromLocation_Finalizer(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        System.Exception __exception)
    {
        if (__exception == null || !IsHosting)
        {
            return __exception;
        }

        MultiplayerPlugin.Logger?.LogError(
            $"[SC] SpawnFromLocation THREW src='{(multiballGameObject != null ? multiballGameObject.name : "<null>")}' caller={__instance?.GetType().Name}: {__exception.GetType().Name}: {__exception.Message}");

        return null;
    }
}
