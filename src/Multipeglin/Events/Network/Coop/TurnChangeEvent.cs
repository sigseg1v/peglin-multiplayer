namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Broadcast from host to all clients when the active turn changes.
/// Clients use this to update their UI and enable/disable aiming.
/// </summary>
public class TurnChangeEvent
{
    public int ActiveSlotIndex { get; set; }

    public string ActivePlayerName { get; set; }

    public string TurnPhase { get; set; }

    public int RoundNumber { get; set; }
}
