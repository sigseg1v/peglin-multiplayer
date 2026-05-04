using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Pass-through. The host fires SlotConfigEvent locally via Dispatch; this
/// handler returns the event so it gets serialized and broadcast to clients.
/// </summary>
public sealed class SlotConfigServerHandler : IServerHandler<SlotConfigEvent>
{
    public SlotConfigEvent Handle(SlotConfigEvent networkEvent) => networkEvent;
}
