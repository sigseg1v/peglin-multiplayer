using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class RelicStateSnapshot
{
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
    public bool IsEnabled { get; set; }
}
