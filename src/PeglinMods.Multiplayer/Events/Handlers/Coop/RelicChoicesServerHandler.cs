using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RelicChoicesEvent (host -> targeted client).
/// Passes through for broadcast.
/// </summary>
public sealed class RelicChoicesServerHandler : IServerHandler<RelicChoicesEvent>
{
    public RelicChoicesEvent Handle(RelicChoicesEvent networkEvent) => networkEvent;
}
