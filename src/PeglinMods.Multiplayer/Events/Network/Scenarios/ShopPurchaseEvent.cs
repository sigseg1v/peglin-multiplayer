namespace PeglinMods.Multiplayer.Events.Network.Scenarios;

/// <summary>
/// Client -> host: a single shop purchase was made. Sent immediately on each
/// purchase so the host's CoopPlayerState is updated before the next heartbeat,
/// preventing the heartbeat from resetting the client's gold to a stale value.
/// </summary>
public class ShopPurchaseEvent
{
    /// <summary>"orb" or "relic"</summary>
    public string Type { get; set; }

    /// <summary>Prefab name (for orbs) or locKey (for relics).</summary>
    public string Name { get; set; }

    /// <summary>Gold cost of this item.</summary>
    public int Cost { get; set; }

    /// <summary>RelicEffect enum value (for relics only, -1 for orbs).</summary>
    public int RelicEffect { get; set; } = -1;
}
