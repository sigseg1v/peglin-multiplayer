namespace Multipeglin.Events.Network;

/// <summary>
/// Represents an event received from a peer that this client doesn't have
/// a handler for. Used for forward-compatibility logging.
/// </summary>
public sealed class UnknownEvent
{
    public string OriginalTypeId { get; set; }

    public string RawPayload { get; set; }
}
