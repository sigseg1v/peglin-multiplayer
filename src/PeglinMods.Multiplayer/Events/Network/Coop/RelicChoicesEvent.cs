using System.Collections.Generic;
using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Network.Coop;

public class RelicChoicesEvent
{
    public int TargetSlotIndex { get; set; }
    public List<RelicEntry> Choices { get; set; } = new List<RelicEntry>();
}
