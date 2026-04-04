using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for AllChoicesCompleteEvent (host -> all clients).
/// Passes through for broadcast.
/// </summary>
public sealed class AllChoicesCompleteServerHandler : IServerHandler<AllChoicesCompleteEvent>
{
    public AllChoicesCompleteEvent Handle(AllChoicesCompleteEvent networkEvent) => networkEvent;
}
