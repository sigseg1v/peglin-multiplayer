namespace Multipeglin.Events.Network.Enemy;

public class EnemyMovedEvent
{
    public string EnemyId { get; set; }

    public int FromSlot { get; set; }

    public int ToSlot { get; set; }

    public float PosX { get; set; }

    public float PosY { get; set; }
}
