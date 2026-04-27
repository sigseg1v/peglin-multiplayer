using System.Collections.Generic;
using Battle.Attacks;

namespace Multipeglin.CustomOrbs;

/// <summary>
/// Stack-based per-Attack temporary damage swap for Beast Warb. Push before
/// <see cref="Attack.Fire"/> writes its return value, pop after.
/// </summary>
internal static class BeastWarbDamageSwap
{
    private struct Saved
    {
        public int Damage;
        public int CritDamage;
    }

    private static readonly Dictionary<Attack, Stack<Saved>> Stacks = new();

    public static void Push(Attack attack, bool isBoss, BeastWarbBehaviour beh)
    {
        if (!Stacks.TryGetValue(attack, out var stack))
        {
            stack = new Stack<Saved>();
            Stacks[attack] = stack;
        }

        stack.Push(new Saved { Damage = attack.DamagePerPeg, CritDamage = attack.CritDamagePerPeg });

        if (isBoss)
        {
            attack.DamagePerPeg = beh.BossDamage;
            attack.CritDamagePerPeg = beh.BossCritDamage;
        }
        else
        {
            attack.DamagePerPeg = beh.NormalDamage;
            attack.CritDamagePerPeg = beh.NormalCritDamage;
        }
    }

    public static void Pop(Attack attack)
    {
        if (!Stacks.TryGetValue(attack, out var stack) || stack.Count == 0)
        {
            return;
        }

        var saved = stack.Pop();
        attack.DamagePerPeg = saved.Damage;
        attack.CritDamagePerPeg = saved.CritDamage;
    }
}
