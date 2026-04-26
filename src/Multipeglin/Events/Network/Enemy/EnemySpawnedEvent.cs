namespace Multipeglin.Events.Network.Enemy;

public class EnemySpawnedEvent
{
    public string EnemyId { get; set; }

    public string LocKey { get; set; }

    public float CurrentHealth { get; set; }

    public float MaxHealth { get; set; }

    public int SlotIndex { get; set; }

    public float MeleeDamage { get; set; }

    public float RangedDamage { get; set; }
}
