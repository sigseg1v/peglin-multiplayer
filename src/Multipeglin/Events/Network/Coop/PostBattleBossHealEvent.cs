using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host -> client. Dispatched after the host detects a boss-room post-battle
/// heal. Vanilla <c>BattleUpgradeCanvas.ConfigureForPostBattleRewards</c> only
/// heals the active singleton (host's currently-loaded slot), so non-active
/// coop slots never get their post-boss heal. This event carries the new
/// CurrentHealth/MaxHealth for every slot so clients can apply the heal to
/// their own slot (with animation) and host-side stored CoopPlayerStates stay
/// authoritative.
/// </summary>
public class PostBattleBossHealEvent
{
    public List<Entry> Entries { get; set; } = new List<Entry>();

    public class Entry
    {
        public int SlotIndex { get; set; }

        public float NewCurrentHealth { get; set; }

        public float MaxHealth { get; set; }

        public float HealedAmount { get; set; }
    }
}
