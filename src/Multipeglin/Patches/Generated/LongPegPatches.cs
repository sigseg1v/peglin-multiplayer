using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class LongPegPatches
{
    /// <summary>
    /// HOST: when a LongPeg's delayed-death timer fires and the peg switches to
    /// its cleared material, immediately fade it out like the client does. The
    /// native game only fades LongPegs at end-of-battle via RemoveClearedPegs,
    /// so without this hook the host sees popped rectangular pegs sit on the
    /// board greyed-out while the client has already faded them to alpha=0.
    /// </summary>
    [HarmonyPatch(typeof(LongPeg), "SetActiveStatus")]
    [HarmonyPostfix]
    public static void LongPeg_SetActiveStatus_Postfix(LongPeg __instance, bool active)
    {
        if (active)
        {
            return;
        }

        if (!IsHosting)
        {
            return;
        }

        try
        {
            var clearedField = HarmonyLib.AccessTools.Field(typeof(global::Peg), "_cleared");
            var isCleared = (bool)(clearedField?.GetValue(__instance) ?? false);
            if (isCleared)
            {
                __instance.RemoveIfCleared();
            }
        }
        catch { }
    }
}
