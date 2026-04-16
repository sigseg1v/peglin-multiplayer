namespace Multipeglin.Events.Network.Battle;

public class AttackStartedEvent
{
    /// <summary>Animator trigger for the peglin attack animation (e.g. "attack").</summary>
    public string AnimTrigger { get; set; }

    /// <summary>GUID of the target enemy being attacked.</summary>
    public string TargetEnemyGuid { get; set; }

    /// <summary>Number of pegs hit this shot — controls projectile size scaling.</summary>
    public int NumPegsHit { get; set; }

    /// <summary>Whether this is a critical hit (uses different projectile visual).</summary>
    public bool IsCrit { get; set; }

    /// <summary>Name of the orb being fired (for sprite lookup).</summary>
    public string OrbName { get; set; }
}
