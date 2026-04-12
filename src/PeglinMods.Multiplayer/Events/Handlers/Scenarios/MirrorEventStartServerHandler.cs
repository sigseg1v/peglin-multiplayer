using PeglinMods.Multiplayer.Events.Network.Scenarios;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for MirrorEventStartEvent (host → clients).
/// Passes through to broadcast to all clients.
/// </summary>
public sealed class MirrorEventStartServerHandler : IServerHandler<MirrorEventStartEvent>
{
    public MirrorEventStartEvent Handle(MirrorEventStartEvent networkEvent) => networkEvent;
}
