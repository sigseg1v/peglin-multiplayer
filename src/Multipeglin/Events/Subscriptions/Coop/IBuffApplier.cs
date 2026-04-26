using Multipeglin.GameState;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Resolves a stored CoopPlayerState's defensive status effects against a raw
/// damage value. Encapsulates the Ballusion / Intangiball / Ballwark pipeline
/// previously inlined in CoopSubscriptions.
/// </summary>
public interface IBuffApplier
{
    /// <summary>
    /// Apply Ballusion (dodge), Intangiball (cap), Ballwark (armour) against
    /// rawDamage using the slot's stored status effects and relics. Modifies
    /// the slot's status effect list in-place (reducing dodge stacks on a
    /// successful dodge, consuming armour, etc.). Returns the effective damage.
    /// </summary>
    float ApplyDefensiveBuffs(CoopPlayerState state, float rawDamage);
}
