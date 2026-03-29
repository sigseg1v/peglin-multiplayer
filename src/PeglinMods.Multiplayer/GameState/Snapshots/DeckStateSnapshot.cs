using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class DeckStateSnapshot
{
    public List<OrbEntry> CompleteDeck { get; set; } = new List<OrbEntry>();
    public List<OrbEntry> BattleDeck { get; set; } = new List<OrbEntry>();
    public string CurrentOrb { get; set; }
    public int CurrentOrbLevel { get; set; }
    public int DeckSize { get; set; }
}

public class OrbEntry
{
    public string Guid { get; set; }
    public int DeckIndex { get; set; }
    public string Name { get; set; }
    public string LocName { get; set; }
    public int Level { get; set; }
    public int BaseDamage { get; set; }
    public int CritDamage { get; set; }
}
