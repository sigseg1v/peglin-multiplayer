namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host -> targeted client. The host-rolled treasure-room relic for one slot.
/// Each slot gets an independently-rolled relic so that all clients see different
/// offers in the "?" / treasure room, while keeping the host authoritative.
///
/// The patch on BattleUpgradeCanvas.SetupRelicGrant consumes this: when present
/// for the local slot, it shows the host-chosen relic instead of rolling its own
/// (which would otherwise be identical across all clients due to shared seed).
/// </summary>
public class CoopTreasureRelicChoiceEvent
{
    public int TargetSlotIndex { get; set; }
    public string RelicName { get; set; }
    public int Rarity { get; set; }
}
