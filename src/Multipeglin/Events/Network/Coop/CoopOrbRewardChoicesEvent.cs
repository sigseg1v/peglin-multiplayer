using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host -> targeted client. Per-slot orb suggestion list for the post-battle
/// "Add Orb" panel. Each slot gets an independently-rolled list so that all
/// players see different orb offers, while keeping the host authoritative
/// over what was rolled.
///
/// The patch on PopulateSuggestionOrbs.GenerateAddableOrbs consumes this:
/// when present for the local slot, it skips the native (seeded) roll and
/// populates the buttons from this list instead.
/// </summary>
public class CoopOrbRewardChoicesEvent
{
    public int TargetSlotIndex { get; set; }

    public List<string> OrbPrefabNames { get; set; } = new List<string>();
}
