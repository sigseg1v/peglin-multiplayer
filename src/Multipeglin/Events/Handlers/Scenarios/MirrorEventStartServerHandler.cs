using Multipeglin.Events.Network.Scenarios;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for MirrorEventStartEvent (host → clients).
/// Passes through to broadcast to all clients.
/// </summary>
public sealed class MirrorEventStartServerHandler : IServerHandler<MirrorEventStartEvent>
{
    public MirrorEventStartEvent Handle(MirrorEventStartEvent networkEvent) => networkEvent;
}
