using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

/// <summary>
/// Coop fixes for SummoningCirclePachinkoBall.
///
/// 1. Softlock: SC clones an orb from deckManager.shuffledDeck (parented to
///    battleDeckOrbContainerInstance, which can be inactive); Instantiate
///    preserves activeSelf so satellites spawn inactive, never tick, never
///    fire OnPachinkoBallDestroyed, and the BC counter hangs forever.
///
/// 2. Satellites falling straight down: Rigidbody2D.AddForce inside
///    SpawnMultiballFromLocation is silently ignored when the instantiated
///    GameObject is inactive (because it was cloned from an inactive prefab).
///    The previous postfix re-activated the satellite, but by then the force
///    had already been absorbed by the disabled rigidbody — leaving the ball
///    with zero velocity, so gravity dropped it straight down.
///
/// Fix: prefix temporarily activates the source prefab BEFORE Instantiate so
/// AddForce lands on a live rigidbody. Postfix restores the prefab's prior
/// activeSelf (and forces the spawned satellite active as a defense in depth).
/// Finalizer swallows any exception so a single broken prefab can't kill the
/// whole FireOrbs coroutine.
/// </summary>
[HarmonyPatch]
internal static class SummoningCirclePatches
{
    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyPrefix]
    public static void PachinkoBall_SpawnMultiballFromLocation_Prefix(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        out bool __state)
    {
        __state = false;

        if (!IsHosting)
        {
            return;
        }

        if (__instance is not SummoningCirclePachinkoBall || multiballGameObject == null)
        {
            return;
        }

        if (!multiballGameObject.activeSelf)
        {
            multiballGameObject.SetActive(true);
            __state = true;
        }
    }

    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyPostfix]
    public static void PachinkoBall_SpawnMultiballFromLocation_Postfix(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        UnityEngine.GameObject __result,
        bool __state)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance is not SummoningCirclePachinkoBall)
        {
            if (__state && multiballGameObject != null)
            {
                multiballGameObject.SetActive(false);
            }

            return;
        }

        if (__result != null && !__result.activeSelf)
        {
            __result.SetActive(true);
        }

        // Restore the source prefab's prior activeSelf so we don't leak the
        // temporary activation into other systems that inspect deck orb prefabs.
        if (__state && multiballGameObject != null)
        {
            multiballGameObject.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyFinalizer]
    public static System.Exception PachinkoBall_SpawnMultiballFromLocation_Finalizer(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        System.Exception __exception,
        bool __state)
    {
        // Ensure the source prefab's activeSelf is restored even if the
        // original method threw — otherwise a leaked activation could surprise
        // other systems that inspect deck orb prefabs.
        if (__state && multiballGameObject != null && multiballGameObject.activeSelf)
        {
            multiballGameObject.SetActive(false);
        }

        if (__exception == null || !IsHosting)
        {
            return __exception;
        }

        MultiplayerPlugin.Logger?.LogError(
            $"[SC] SpawnFromLocation THREW src='{(multiballGameObject != null ? multiballGameObject.name : "<null>")}' caller={__instance?.GetType().Name}: {__exception.GetType().Name}: {__exception.Message}");

        return null;
    }
}
