namespace Multipeglin.Events.Network.Cursor;

/// <summary>
/// Broadcast the local player's cursor world position. Bidirectional — host and
/// clients both send this when their cursor moves, and the peer on the other
/// side renders a ghost cursor. World coords so the indicator tracks the same
/// spot in the game regardless of each side's window size.
/// </summary>
public class CursorPositionEvent
{
    /// <summary>Slot of the sender (host=0, first client=1, etc).</summary>
    public int FromSlot { get; set; }

    public float WorldX { get; set; }

    public float WorldY { get; set; }
}
