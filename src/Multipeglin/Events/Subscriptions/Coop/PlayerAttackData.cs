using System.Collections.Generic;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Data for a single player's shot, consumed during DoAttack.
/// </summary>
public class PlayerAttackData
{
    public int SlotIndex;
    public string PlayerName;
    public long Damage;
    public string TargetEnemyGuid;
    public bool IsAoE;
    public bool IsHeal;
    public List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> StatusEffectsToApply;
    public int NumPegsHit;
    public int CriticalHitCount;
    public string OrbPrefabName;

    // Pincer Maneuver: damage above is the halved primary shot; the coop
    // sequencer must apply the same damage to the farthest enemy.
    public bool HasReverseShot;

    // Alien's Rock — splash to adjacent enemies on targeted hit.
    public bool HasTargetedSplash;

    // TARGETED_ATTACKS_HIT_ALL — targeted hit damages every alive enemy.
    public bool HasTargetedHitAll;
}
