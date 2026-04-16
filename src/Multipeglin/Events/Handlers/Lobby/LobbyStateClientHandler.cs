namespace Multipeglin.Events.Handlers.Lobby;

using Multipeglin.Events.Network.Lobby;
using Multipeglin.UI;

public sealed class LobbyStateClientHandler : IClientHandler<LobbyStateEvent>
{
    public void Handle(LobbyStateEvent networkEvent)
    {
        LobbyUI.ApplyLobbyState(networkEvent);
    }
}
