namespace Multipeglin.Events.Handlers.Lobby;

using Multipeglin.Events.Network.Lobby;

public sealed class LobbyStateServerHandler : IServerHandler<LobbyStateEvent>
{
    public LobbyStateEvent Handle(LobbyStateEvent networkEvent) => networkEvent;
}
