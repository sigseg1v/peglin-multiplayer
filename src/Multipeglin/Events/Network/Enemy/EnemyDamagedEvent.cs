namespace Multipeglin.Events.Network.Enemy;

public class EnemyDamagedEvent
{
    public string EnemyId { get; set; }

    public long Damage { get; set; }

    public int DamageSource { get; set; }

    public float RemainingHealth { get; set; }
}
