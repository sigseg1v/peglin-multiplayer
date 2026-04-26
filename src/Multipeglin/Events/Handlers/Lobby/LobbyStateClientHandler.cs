using Multipeglin.Events.Network.Lobby;
using Multipeglin.UI;

namespace Multipeglin.Events.Handlers.Lobby;

public sealed class LobbyStateClientHandler : IClientHandler<LobbyStateEvent>
{
    public void Handle(LobbyStateEvent networkEvent)
    {
        LobbyUI.ApplyLobbyState(networkEvent);
    }
}
