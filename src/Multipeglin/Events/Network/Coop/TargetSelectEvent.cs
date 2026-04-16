namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Client → host: informs the host which enemy the client has selected as their
/// attack target. Sent whenever the client changes their target selection during
/// their aiming phase. The host uses this to display a targeting indicator.
/// </summary>
public class TargetSelectEvent
{
    /// <summary>GUID of the enemy the client is targeting. Null means "no target".</summary>
    public string TargetEnemyGuid { get; set; }
}
