using System.Collections.Generic;
using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Network.Coop;

public class RelicChoicesEvent
{
    public int TargetSlotIndex { get; set; }

    public List<RelicEntry> Choices { get; set; } = new List<RelicEntry>();
}
