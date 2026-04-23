using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

public class EnemyStateSnapshot
{
    public List<EnemyEntry> Enemies { get; set; } = new List<EnemyEntry>();
    public int BattleState { get; set; }
    public string BattleStateName { get; set; }
    public int RoundCount { get; set; }

    /// <summary>Upcoming enemy prefab names from EnemyInfoManager (visual preview list).</summary>
    public List<string> UpcomingEnemyNames { get; set; } = new List<string>();

    /// <summary>BattleController.pachinkoBallSpawnLocation. Bosses like SlimeBoss mutate this
    /// each turn to alternate aim origin; the client must mirror the host's value so its
    /// ball-spawn position matches during the client's own aim turn.</summary>
    public float BallSpawnX { get; set; }
    public float BallSpawnY { get; set; }
}

public class EnemyEntry
{
    public string Id { get; set; }
    public string LocKey { get; set; }
    public string EnemyName { get; set; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public float MeleeDamage { get; set; }
    public float RangedDamage { get; set; }
    public int ChargeTime { get; set; }
    public int CurrentCharge { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public int SlotIndex { get; set; }
    public bool IsFlying { get; set; }
    public List<StatusEffectEntry> StatusEffects { get; set; } = new List<StatusEffectEntry>();

    /// <summary>
    /// ShieldEnemy barricade state (ShieldKnight etc). The BarricadeEnemy is a separate
    /// Enemy instance attached via ShieldEnemy._shield, not tracked in EnemyManager.Enemies —
    /// so it must be ridden along on its parent's entry.
    /// </summary>
    public bool HasShield { get; set; }
    public float ShieldCurrentHealth { get; set; }
    public float ShieldMaxHealth { get; set; }
    /// <summary>True while the shield GameObject is active on the host — flips to false once the barricade dies.</summary>
    public bool ShieldActive { get; set; }
}
