using System.Collections.Generic;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>Per-player peg damage data accumulated during shots.</summary>
internal sealed class ShotDamageData
{
    public int PegMultiplierDamageTally;
    public int CriticalHitCount;
    public int NumPegsHit;
    public int CactusDamageTally;
    public float DamageMultiplier;
    public long DamageBonus;

    // Per-player targeting data
    public long PrecomputedDamage;
    public string TargetEnemyGuid;
    public bool IsAoE;      // SimpleAttack hits all enemies
    public bool IsHeal;     // HealAction — not an attack
    public string PlayerName;

    // Status effects captured from the orb's IAffectEnemyOnHit components + relic effects
    public List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> StatusEffectsToApply;

    // Orb name (prefab name without "(Clone)") for re-instantiating at ALL_DONE.
    // Used with AssetLoading.GetOrbPrefab() to get the correct orb type so DoAttack
    // computes damage with the right formula.
    public string OrbPrefabName;

    // Pincer Maneuver (ADDITIONAL_REVERSE_PROJECTILE_ATTACK): when true, the primary
    // damage has already been halved (rounded up) and a second shot of the same
    // halved damage should land on the farthest enemy from the player.
    public bool HasReverseShot;

    // Alien's Rock (SPLASH_EFFECT_ON_TARGETED_ATTACKS): targeted attacks splash to
    // adjacent enemies (range 1, SIDE). Captured at shot time using the shooter's
    // active relics; applied during PlayCoopAttackSequence by expanding targets.
    public bool HasTargetedSplash;
    // TARGETED_ATTACKS_HIT_ALL: targeted attacks hit every alive enemy.
    public bool HasTargetedHitAll;
}
