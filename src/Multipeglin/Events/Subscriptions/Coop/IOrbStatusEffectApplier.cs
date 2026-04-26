using System.Collections.Generic;
using Battle;
using Battle.Attacks;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Status-effect / orb-restoration helpers used by the coop attack pipeline.
/// These wrap the per-orb pieces of native ProjectileAttack.DoDamage and
/// CallPostAttackOperations that get bypassed when the DoAttack Harmony prefix
/// returns false in coop.
/// </summary>
public interface IOrbStatusEffectApplier
{
    /// <summary>
    /// Capture status effects from an orb at shot completion time for non-host
    /// players. Combines attack.GetStatusEffects(crit) with per-hit
    /// AddStatusEffectOnHit / AddRandomStatusEffectOnHit components. Returns
    /// null when no effects were collected.
    /// </summary>
    List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> CaptureOrbStatusEffects(Attack attack, int critCount);

    /// <summary>
    /// Apply self-granting post-attack status effects (Ballusion from Evasive
    /// Maneuvorb, Muscircle, Ballwark from shield pegs, etc.) to the currently
    /// loaded player's PlayerStatusEffectController.
    /// </summary>
    void ApplySelfPostAttackBuffs(Attack attack, int critCount, int slotIndex);

    /// <summary>
    /// Instantiate the orb prefab off-screen, initialize its Attack component
    /// against the currently loaded singletons, and set it as
    /// AttackManager._attack. Used at ALL_DONE to restore the host's orb so
    /// DoAttack uses the correct damage formula.
    /// </summary>
    void RestoreAttackFromPrefab(BattleController bc, GameObject orbPrefab, string label);

    /// <summary>Destroy any temporary orb instance created by RestoreAttackFromPrefab.</summary>
    void CleanupTempOrb();
}
