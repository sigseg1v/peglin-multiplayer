namespace Multipeglin.Events.Network.Health;

public class PlayerHealedEvent
{
    public float Amount { get; set; }

    public float RemainingHealth { get; set; }

    // Coop slot the heal applied to. -1 when not in coop / unknown.
    public int TargetSlotIndex { get; set; } = -1;
}
