namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Client -> host: a player's nav ball landed in a slot. ChildIndex is the
/// resolved index into StaticGameData.currentNode.ChildNodes (0..N-1).
/// Server handler tallies votes and broadcasts NavigateResolvedEvent when done.
/// </summary>
public class NavigateVoteEvent
{
    public int ChildIndex { get; set; }
}
