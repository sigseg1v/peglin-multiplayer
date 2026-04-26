using Multipeglin.Events.Network.Lobby;

namespace Multipeglin.Events.Handlers.Lobby;

public sealed class LobbyStateServerHandler : IServerHandler<LobbyStateEvent>
{
    public LobbyStateEvent Handle(LobbyStateEvent networkEvent) => networkEvent;
}
