namespace Multipeglin.Events.Network.Enemy;

public class EnemyAttackEvent
{
    public string EnemyId { get; set; }

    public float Damage { get; set; }

    public bool IsMelee { get; set; }

    public int DamageSource { get; set; }
}
