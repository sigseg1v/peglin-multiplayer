using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class PlayerStateSnapshot
{
    /// <summary>
    /// In coop mode, identifies which player slot this snapshot belongs to.
    /// -1 means unspecified (single-player or legacy).
    /// </summary>
    public int ActiveSlotIndex { get; set; } = -1;

    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public int Gold { get; set; }
    public List<StatusEffectEntry> StatusEffects { get; set; } = new List<StatusEffectEntry>();
    public bool IsSpedUp { get; set; }
    public float SpeedupLevel { get; set; } = 2f;
}

public class StatusEffectEntry
{
    public int EffectType { get; set; }
    public string EffectName { get; set; }
    public int Intensity { get; set; }
}
