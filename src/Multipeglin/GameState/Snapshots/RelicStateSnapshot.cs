using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

public class RelicStateSnapshot
{
    /// <summary>
    /// In coop mode, identifies which player slot this snapshot belongs to.
    /// -1 means unspecified (single-player or legacy).
    /// </summary>
    public int ActiveSlotIndex { get; set; } = -1;

    public List<RelicEntry> OwnedRelics { get; set; } = new List<RelicEntry>();
    public int TotalRelicCount { get; set; }
}

public class RelicEntry
{
    public int Effect { get; set; }
    public string EffectName { get; set; }
    public string LocKey { get; set; }
    public int Rarity { get; set; }
    public int RemainingCountdown { get; set; }
    public int RemainingUsesPerShot { get; set; }
    public int RemainingUsesPerBattle { get; set; }
    public int RemainingUsesPerRun { get; set; }
    public bool IsEnabled { get; set; }
}
