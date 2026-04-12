namespace PeglinMods.Multiplayer.Events.Network.Coop;

/// <summary>
/// Client → host: requests that the currently aimed orb be discarded during the client's turn.
/// The host validates (correct turn, discards remaining) and calls AttemptOrbDiscard().
/// </summary>
public class OrbDiscardRequestEvent
{
}
