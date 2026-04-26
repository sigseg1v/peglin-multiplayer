
using Multipeglin.Events.Network.Lobby;

namespace Multipeglin.Events.Handlers.Lobby;
/// <summary>
/// On host: receives ClassSelectEvent from a client, updates PlayerRegistry,
/// and broadcasts updated LobbyStateEvent. The Dispatch call in the registry
/// broadcasts the returned event to all peers.
///
/// The actual PlayerRegistry update and LobbyState broadcast happens in
/// LobbyManager.HandleClassSelect(), called from the client handler side
/// when the host receives this event.
/// </summary>
public sealed class ClassSelectServerHandler : IServerHandler<ClassSelectEvent>
{
    /// <summary>
    /// Pass through -- the client handler on the host will process this
    /// and trigger a LobbyState broadcast.
    /// </summary>
    public ClassSelectEvent Handle(ClassSelectEvent networkEvent) => null; // Don't rebroadcast raw event
}
