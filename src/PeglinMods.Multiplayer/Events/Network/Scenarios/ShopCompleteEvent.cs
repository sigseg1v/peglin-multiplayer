using System.Collections.Generic;

namespace PeglinMods.Multiplayer.Events.Network.Scenarios;

/// <summary>
/// Client -> host: the client has finished shopping and clicked "Exit Store".
/// Host applies purchases to the client's CoopPlayerState.
/// </summary>
public class ShopCompleteEvent
{
    /// <summary>Items purchased by the client.</summary>
    public List<ShopPurchase> Purchases { get; set; } = new List<ShopPurchase>();

    /// <summary>Total gold spent during the shop visit.</summary>
    public int GoldSpent { get; set; }

    /// <summary>Client's remaining gold after all purchases.</summary>
    public int RemainingGold { get; set; }
}

public class ShopPurchase
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
