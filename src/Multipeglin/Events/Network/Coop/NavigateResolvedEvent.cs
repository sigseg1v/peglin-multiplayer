using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host -> clients: navigate phase concluded with the given winning child index.
/// Clients should clean up local nav UI; the actual scene transition arrives via
/// the existing scene-load sync (host's TriggerVictory / FadeAndLoad).
/// VoteCounts is per-child-index, used to update the tally HUD on clients.
/// </summary>
public class NavigateResolvedEvent
{
    public int ChosenChildIndex { get; set; }

    /// <summary>Final vote counts, indexed by child index (length = ChildNodeCount).</summary>
    public List<int> VoteCounts { get; set; }
}
