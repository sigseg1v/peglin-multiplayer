using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class PlayerStateSnapshot
{
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public int Gold { get; set; }
    public List<StatusEffectEntry> StatusEffects { get; set; } = new List<StatusEffectEntry>();
    public bool IsSpedUp { get; set; }
}

public class StatusEffectEntry
{
    public int EffectType { get; set; }
    public string EffectName { get; set; }
    public int Intensity { get; set; }
}
