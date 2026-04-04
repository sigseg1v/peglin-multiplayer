using System.Collections.Generic;

namespace PeglinMods.Multiplayer.Events.Network.Coop;

public class RewardChoicesEvent
{
    public int TargetSlotIndex { get; set; }
    public List<RewardOption> Options { get; set; } = new List<RewardOption>();
}

public class RewardOption
{
    public int OptionIndex { get; set; }
    public string Type { get; set; }        // "relic", "orb_upgrade", "orb_add", "heal", "max_hp", "skip"
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public int RelicEffect { get; set; }    // Only for relic type
    public int GoldReward { get; set; }     // Only for skip type
}
