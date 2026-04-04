namespace PeglinMods.Multiplayer.Events.Network.Coop;

/// <summary>
/// Sent from a client to the host when they fire their shot.
/// The host validates it's the sender's turn and executes the shot.
/// </summary>
public class ShootRequestEvent
{
    public float AimDirectionX { get; set; }
    public float AimDirectionY { get; set; }
}
