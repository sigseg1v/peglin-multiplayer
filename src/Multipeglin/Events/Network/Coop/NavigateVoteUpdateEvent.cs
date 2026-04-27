using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host -> clients: live vote tally update so every player can color slots.
/// Sent each time a vote is recorded (host or client). Clients render the
/// per-slot color (green=winner, red=loser, yellow=zero).
/// </summary>
public class NavigateVoteUpdateEvent
{
    /// <summary>Final vote counts, indexed by child index.</summary>
    public List<int> VoteCounts { get; set; }
}
