using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class EnemyStateSnapshot
{
    public List<EnemyEntry> Enemies { get; set; } = new List<EnemyEntry>();
    public int BattleState { get; set; }
    public string BattleStateName { get; set; }
    public int RoundCount { get; set; }

    /// <summary>Number of upcoming wave enemies remaining (from EnemyInfoManager).</summary>
    public int UpcomingEnemyCount { get; set; }
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
}
