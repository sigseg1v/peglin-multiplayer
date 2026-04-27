namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host -> clients: signals that the end-of-stage navigate phase has begun.
/// All players should arm their nav ball locally and shoot. Slot hits become
/// votes (NavigateVoteEvent). The host tallies and resolves with NavigateResolvedEvent.
///
/// Source values:
///   "post_battle" - end of battle, PostBattleController.StartNavigation
///   "nav_only"    - end of event/treasure/shop/text/peg-minigame, NavOnlyController.PrepareForNavigation
/// </summary>
public class NavigatePhaseStartEvent
{
    public string Source { get; set; }

    /// <summary>Number of available child node choices (1..3). Drives slot tallying.</summary>
    public int ChildNodeCount { get; set; }
}
