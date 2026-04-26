using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host → clients: the boss/rare relic choices generated on the host's
/// BattleUpgradeCanvas so clients display the same relics.
/// </summary>
public class PostBattleRelicChoicesEvent
{
    public List<RelicChoiceEntry> Choices { get; set; } = new List<RelicChoiceEntry>();
}

public class RelicChoiceEntry
{
    public int Effect { get; set; }

    public string LocKey { get; set; }
}
