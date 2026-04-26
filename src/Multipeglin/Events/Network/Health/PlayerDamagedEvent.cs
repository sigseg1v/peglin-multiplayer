namespace Multipeglin.Events.Network.Health;

public class PlayerDamagedEvent
{
    public float Damage { get; set; }

    public float RemainingHealth { get; set; }

    public float MaxHealth { get; set; }
}
