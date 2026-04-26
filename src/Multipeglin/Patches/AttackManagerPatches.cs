using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class AttackManagerPatches
{
    /// <summary>Capture attack trigger, target enemy, peg count, crit, and orb name when attack starts.</summary>
    [HarmonyPatch(typeof(Battle.Attacks.AttackManager), "Attack")]
    [HarmonyPostfix]
    public static void AttackManager_Attack_Postfix(
        Battle.Attacks.AttackManager __instance,
        Battle.Enemies.Enemy target,
        int numPegsHitThisShot,
        int criticalHitCount)
    {
        if (!IsHosting)
        {
            return;
        }

        try
        {
            var attackField = HarmonyLib.AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
            var attack = attackField?.GetValue(__instance) as Battle.Attacks.Attack;
            LastAttackAnimTrigger = attack?.PeglinAttackAnimationTrigger ?? "attack";
            LastAttackNumPegsHit = numPegsHitThisShot;
            LastAttackIsCrit = criticalHitCount > 0;
            LastAttackOrbName = attack?.gameObject?.name?.Replace("(Clone)", string.Empty).Trim();

            if (target != null)
            {
                var enemyId = MultiplayerPlugin.Services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true ? eid : null;
                LastAttackTargetGuid = enemyId?.GetGuid(target);
            }
        }
        catch
        {
        }
    }

    [HarmonyPatch(typeof(Battle.Attacks.AttackManager), "Attack")]
    [HarmonyPrefix]
    public static bool AttackManager_Attack_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked AttackManager.Attack on client");
        return false;
    }
}
