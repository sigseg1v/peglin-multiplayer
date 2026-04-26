using Battle;
using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PegCoinOverlayPatches
{
    // GOLD_ADDS_TO_DAMAGE (Peglin's Cup relic) adds +1 to _pegMultiplierDamageTally
    // on every coin-peg collection during a shot. This can make damage totals look
    // "doubled" vs. the visible peg-hit count — flag it explicitly so logs don't
    // get mistaken for a bug. See PegCoinOverlay.TriggerCoinCollected (decomp).
    [HarmonyPatch(typeof(Battle.PegBehaviour.PegCoinOverlay), "TriggerCoinCollected")]
    [HarmonyPostfix]
    public static void PegCoinOverlay_TriggerCoinCollected_GoldLog(
        Battle.PegBehaviour.PegCoinOverlay __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        try
        {
            if (BattleController.CurrentBattleState != BattleController.BattleState.AWAITING_SHOT_COMPLETION)
            {
                return;
            }

            var pegField = AccessTools.Field(typeof(Battle.PegBehaviour.PegCoinOverlay), "_peg");
            var peg = pegField?.GetValue(__instance) as Peg;
            if (peg == null)
            {
                return;
            }

            if (peg.relicManager == null)
            {
                return;
            }

            if (!peg.relicManager.RelicEffectActive(Relics.RelicEffect.GOLD_ADDS_TO_DAMAGE))
            {
                return;
            }

            var pegName = peg.gameObject != null ? peg.gameObject.name : "?";
            MultiplayerPlugin.Logger?.LogInfo(
                $"[Relic] GOLD_ADDS_TO_DAMAGE triggered on peg '{pegName}' (+1 peg tally)");
        }
        catch { }
    }
}
