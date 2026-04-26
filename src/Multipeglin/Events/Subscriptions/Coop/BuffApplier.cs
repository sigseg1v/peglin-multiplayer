using BepInEx.Logging;
using Multipeglin.GameState;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Default IBuffApplier implementation. Mirrors the dodge/cap/armour pipeline
/// the native PlayerHealthController runs against the active player, but
/// against a stored CoopPlayerState's serialized status effects.
/// </summary>
internal sealed class BuffApplier : IBuffApplier
{
    private readonly ManualLogSource _log;

    public BuffApplier(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>
    /// Apply a player's defensive status effects (Ballusion, Intangiball, Ballwark)
    /// to incoming damage. Modifies the player's saved status effects in-place
    /// (e.g. consuming Ballwark stacks, reducing Ballusion on dodge).
    /// Returns the effective damage after all defensive buffs.
    /// </summary>
    public float ApplyDefensiveBuffs(CoopPlayerState state, float rawDamage)
    {
        var damage = rawDamage;

        var ballusionStacks = GetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballusion);
        var intangiballCap = GetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Intangiball);
        var ballwarkStacks = GetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballwark);

        // 1. Ballusion (evasion): 1% dodge chance per stack
        if (ballusionStacks > 0 && damage > 0f)
        {
            var evasionChance = ballusionStacks * Battle.StatusEffects.StatusEffectData.BALLUSION_EVASION_PER_STACK;
            var roll = UnityEngine.Random.value;
            if (roll < evasionChance)
            {
                // Dodge! Reduce Ballusion stacks by ~50%
                var reduction = Battle.StatusEffects.StatusEffectData.BALLUSION_REDUCTION_PERCENTAGE;

                // Check for relics that modify reduction
                if (HasRelic(state, Relics.RelicEffect.BALLUSION_DOUBLE_MAX) ||
                    HasRelic(state, Relics.RelicEffect.RETAIN_DODGE_BETWEEN_BATTLES))
                {
                    reduction -= 0.1f;
                }

                if (HasRelic(state, Relics.RelicEffect.BALLUSION_DOUBLE_GAIN_AND_LOSS))
                {
                    reduction *= 2f;
                }

                var keep = Mathf.Clamp(1f - reduction, 0f, 1f);
                var newStacks = Mathf.RoundToInt(ballusionStacks * keep);
                SetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballusion, newStacks);

                _log.LogInfo($"[CoopSubs] Slot {state.SlotIndex} DODGED (Ballusion {ballusionStacks}->{newStacks}, roll={roll:F3} < {evasionChance:F3})");
                damage = 0f;
            }
        }

        // 2. Intangiball (damage cap)
        if (intangiballCap > 0 && damage > 0f)
        {
            damage = Mathf.Min(damage, intangiballCap);
        }

        // 3. Ballwark (armor absorption)
        if (ballwarkStacks > 0 && damage > 0f)
        {
            float armour = ballwarkStacks;
            if (armour >= damage)
            {
                armour -= damage;
                damage = 0f;
            }
            else
            {
                damage -= armour;
                armour = 0f;
            }

            SetEffectIntensity(state, (int)Battle.StatusEffects.StatusEffectType.Ballwark, Mathf.RoundToInt(armour));
            _log.LogInfo($"[CoopSubs] Slot {state.SlotIndex} Ballwark absorbed: {ballwarkStacks}->{Mathf.RoundToInt(armour)}");
        }

        return Mathf.Max(0f, damage);
    }

    private static int GetEffectIntensity(CoopPlayerState state, int effectType)
    {
        foreach (var e in state.StatusEffects)
        {
            if (e.EffectType == effectType)
            {
                return e.Intensity;
            }
        }

        return 0;
    }

    private static void SetEffectIntensity(CoopPlayerState state, int effectType, int intensity)
    {
        for (var i = 0; i < state.StatusEffects.Count; i++)
        {
            if (state.StatusEffects[i].EffectType == effectType)
            {
                if (intensity <= 0)
                {
                    state.StatusEffects.RemoveAt(i);
                }
                else
                {
                    state.StatusEffects[i].Intensity = intensity;
                }

                return;
            }
        }

        // Effect not in list yet — add if positive
        if (intensity > 0)
        {
            state.StatusEffects.Add(new SerializedStatusEffect
            {
                EffectType = effectType,
                Intensity = intensity,
            });
        }
    }

    private static bool HasRelic(CoopPlayerState state, Relics.RelicEffect effect)
    {
        var effectInt = (int)effect;
        foreach (var r in state.OwnedRelics)
        {
            if (r.Effect == effectInt)
            {
                return true;
            }
        }

        return false;
    }
}
