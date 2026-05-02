using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

/// <summary>
/// Diagnostics + defensive guard for SummoningCirclePachinkoBall in coop.
///
/// The bug: when the host fires the SC orb, FireOrbs' first for-loop bumps
/// BattleController._remainingPachinkoBalls by N (count of satellites to
/// summon), then the second for-loop calls SpawnMultiballFromLocation N
/// times to actually instantiate them. If SpawnMultiballFromLocation throws
/// on j=0 (e.g. the chosen _orbToSummon is a custom-orb prefab with a broken
/// Init that throws on instantiation), the entire FireOrbs coroutine dies
/// silently — Unity routes the exception to Player.log, NOT BepInEx — and
/// the counter stays at N forever, hanging the turn in
/// AWAITING_SHOT_COMPLETION.
///
/// We log SC's chosen _orbToSummon and instrument SpawnMultiballFromLocation
/// so the next softlock pinpoints which orb is throwing. The Finalizer also
/// swallows any exception, which lets the coroutine continue to the next
/// satellite — partial fire is preferable to a full softlock.
/// </summary>
[HarmonyPatch]
internal static class SummoningCirclePatches
{
    [HarmonyPatch(typeof(SummoningCirclePachinkoBall), "InitSummoningCircle")]
    [HarmonyPostfix]
    public static void SC_InitSummoningCircle_Postfix(SummoningCirclePachinkoBall __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        try
        {
            var orbField = AccessTools.Field(typeof(SummoningCirclePachinkoBall), "_orbToSummon");
            var totalField = AccessTools.Field(typeof(SummoningCirclePachinkoBall), "totalNumberOfCopiesToSummon");
            var orb = orbField?.GetValue(__instance) as UnityEngine.GameObject;
            var total = totalField != null ? (int)totalField.GetValue(__instance) : -1;
            MultiplayerPlugin.Logger?.LogInfo(
                $"[SC] Init: orbToSummon='{(orb != null ? orb.name : "<null>")}' total={total}");
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SC] Init log failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyPrefix]
    public static void PachinkoBall_SpawnMultiballFromLocation_Prefix(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        UnityEngine.Vector3 fireFromPosition,
        UnityEngine.Vector2 fireForce)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance is SummoningCirclePachinkoBall)
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[SC] SpawnFromLocation enter: src='{(multiballGameObject != null ? multiballGameObject.name : "<null>")}' from=({fireFromPosition.x:F2},{fireFromPosition.y:F2}) force=({fireForce.x:F2},{fireForce.y:F2})");
        }
    }

    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyPostfix]
    public static void PachinkoBall_SpawnMultiballFromLocation_Postfix(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        UnityEngine.GameObject __result)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance is not SummoningCirclePachinkoBall)
        {
            return;
        }

        if (__result == null)
        {
            MultiplayerPlugin.Logger?.LogError("[SC] SpawnFromLocation returned null GameObject");
            return;
        }

        var go = __result;
        var pb = go.GetComponent<PachinkoBall>();
        var srcSelf = multiballGameObject != null && multiballGameObject.activeSelf;
        var srcInHier = multiballGameObject != null && multiballGameObject.activeInHierarchy;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[SC] SpawnFromLocation post: result name='{go.name}' activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy} state={(pb != null ? pb.CurrentState.ToString() : "<no-pb>")} dummy={(pb != null ? pb.IsDummy.ToString() : "<no-pb>")} srcSelf={srcSelf} srcInHier={srcInHier}");

        // Root cause: SC instantiates clones of `_orbToSummon` (a GameObject taken
        // from `deckManager.shuffledDeck`). Those source orbs are parented to
        // `battleDeckOrbContainerInstance`, which can be inactive. `Instantiate`
        // preserves the source's `activeSelf`, so when the source is inactive the
        // satellite spawns inactive — its Update never runs, the ball never decays,
        // OnPachinkoBallDestroyed never fires, BC._remainingPachinkoBalls stays > 0,
        // and the turn softlocks. Force the satellite active so its physics tick.
        if (!go.activeSelf)
        {
            go.SetActive(true);
            MultiplayerPlugin.Logger?.LogWarning(
                $"[SC] SpawnFromLocation forced result active: '{go.name}'");
        }
    }

    [HarmonyPatch(typeof(PachinkoBall), nameof(PachinkoBall.SpawnMultiballFromLocation))]
    [HarmonyFinalizer]
    public static System.Exception PachinkoBall_SpawnMultiballFromLocation_Finalizer(
        PachinkoBall __instance,
        UnityEngine.GameObject multiballGameObject,
        System.Exception __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (!IsHosting)
        {
            return __exception;
        }

        // SC is the canonical victim of this — surface the failure loudly so
        // we know which orb prefab is broken, and SWALLOW the exception so the
        // FireOrbs coroutine can continue to the next satellite. Without this
        // finalizer one bad spawn kills the entire coroutine.
        MultiplayerPlugin.Logger?.LogError(
            $"[SC] SpawnFromLocation THREW src='{(multiballGameObject != null ? multiballGameObject.name : "<null>")}' caller={__instance?.GetType().Name}: {__exception.GetType().Name}: {__exception.Message}\n{__exception.StackTrace}");

        return null; // swallow
    }
}
