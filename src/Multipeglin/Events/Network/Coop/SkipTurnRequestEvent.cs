namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Client -> host: "skip my turn". The host validates the sender is the
/// active turn player and then runs CoopSubscriptions.SkipCurrentTurn
/// (records a zero-damage shot, advances TurnManager).
/// </summary>
public class SkipTurnRequestEvent
{
}
