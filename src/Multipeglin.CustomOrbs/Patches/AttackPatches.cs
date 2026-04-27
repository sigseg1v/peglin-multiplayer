using Battle.Attacks;
using Battle.Enemies;
using HarmonyLib;

namespace Multipeglin.CustomOrbs.Patches;

/// <summary>
/// Damage overrides for our custom orbs. Bypasses the 999 clamp in the base
/// implementation by writing directly to <c>__result</c>.
/// </summary>
[HarmonyPatch(typeof(Attack), nameof(Attack.GetModifiedDamagePerPeg))]
internal static class AttackGetModifiedDamagePerPegPatch
{
    private static void Postfix(Attack __instance, int critCount, ref int __result)
    {
        var d9000 = __instance.GetComponent<BigBossD9000Behaviour>();
        if (d9000 != null)
        {
            __result = d9000.CritRolledThisShot ? 9000 : 0;
            return;
        }
    }
}

/// <summary>
/// Beast Warb: swap base/crit damage on the Attack component based on whether
/// the target is a normal enemy or a (mini)boss, then restore after Fire().
/// </summary>
[HarmonyPatch(typeof(Attack), nameof(Attack.Fire))]
internal static class AttackFireBeastWarbPatch
{
    private static void Prefix(Attack __instance, Enemy target, out int __state)
    {
        __state = -1;
        var beast = __instance.GetComponent<BeastWarbBehaviour>();
        if (beast == null || target == null)
        {
            return;
        }

        var isBoss = target.enemyTypes.HasFlag(Enemy.EnemyType.Boss)
                     || target.enemyTypes.HasFlag(Enemy.EnemyType.Miniboss);

        // Encode original values into __state as packed int — but we can't
        // pack two values cleanly; instead stash to static and key on instance.
        BeastWarbDamageSwap.Push(__instance, isBoss, beast);
        __state = 1;
    }

    private static void Postfix(Attack __instance, int __state)
    {
        if (__state == 1)
        {
            BeastWarbDamageSwap.Pop(__instance);
        }
    }
}
