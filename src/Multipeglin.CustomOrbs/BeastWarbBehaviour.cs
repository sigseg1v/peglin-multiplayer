using UnityEngine;

namespace Multipeglin.CustomOrbs;

/// <summary>
/// Marker + per-level damage table for Beast Warb. The boss-vs-normal swap
/// happens in AttackFireBeastWarbPatch, which inspects the hitting orb for
/// this component before <see cref="Battle.Attacks.Attack.Fire"/> runs.
/// </summary>
public class BeastWarbBehaviour : MonoBehaviour
{
    public int NormalDamage = 1;
    public int NormalCritDamage = 1;
    public int BossDamage = 15;
    public int BossCritDamage = 20;
}
