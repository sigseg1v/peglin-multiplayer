using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class RegularPegPatches
{
    /// <summary>
    /// When a RegularPeg is converted to a Bomb, a NEW Bomb GameObject is created
    /// and the old peg is destroyed. Transfer the GUID from the old peg to the new
    /// Bomb so the client can still find it by GUID.
    /// We capture the old peg's GUID in a Prefix (before DestroyPeg touches state)
    /// and forcibly assign one if missing, so the Postfix can always transfer it.
    /// </summary>
    [HarmonyPatch(typeof(RegularPeg), "ConvertPegToType")]
    [HarmonyPrefix]
    public static void RegularPeg_ConvertPegToType_Prefix(RegularPeg __instance, Peg.PegType type, out string __state)
    {
        __state = null;
        if (type != Peg.PegType.BOMB)
        {
            return;
        }

        if (MultiplayerPlugin.Services == null)
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.PegIdentifier>(out var pegId))
        {
            return;
        }

        // Use GetOrAssignGuid so that even if this peg was never captured (e.g. dynamically
        // spawned by a relic/orb behaviour) we still have a stable GUID to hand to the bomb.
        __state = pegId.GetOrAssignGuid(__instance);
    }

    [HarmonyPatch(typeof(RegularPeg), "ConvertPegToType")]
    [HarmonyPostfix]
    public static void RegularPeg_ConvertPegToType_Postfix(RegularPeg __instance, Peg.PegType type, GameObject __result, string __state)
    {
        if (type != Peg.PegType.BOMB || __result == null || __result == __instance.gameObject)
        {
            return;
        }

        if (string.IsNullOrEmpty(__state))
        {
            return;
        }

        if (MultiplayerPlugin.Services == null)
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.PegIdentifier>(out var pegId))
        {
            return;
        }

        var newBomb = __result.GetComponent<Peg>();
        if (newBomb == null)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatch] ConvertPegToType(BOMB) returned GameObject without Peg component at " +
                $"pos=({__instance.transform.position.x:F2},{__instance.transform.position.y:F2}) oldGuid={__state}");
            return;
        }

        pegId.Register(newBomb, __state);
        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientPatch] Transferred peg GUID {__state} from RegularPeg to new Bomb at " +
            $"pos=({newBomb.transform.position.x:F2},{newBomb.transform.position.y:F2})");
    }
}
