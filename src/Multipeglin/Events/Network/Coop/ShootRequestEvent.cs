namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Sent from a client to the host when they fire their shot.
/// The host validates it's the sender's turn and executes the shot.
/// </summary>
public class ShootRequestEvent
{
    public float AimDirectionX { get; set; }

    public float AimDirectionY { get; set; }

    /// <summary>
    /// GUID of the enemy the player has targeted for their attack.
    /// Null or empty means "use auto-select" (closest enemy).
    /// </summary>
    public string TargetEnemyGuid { get; set; }
}
