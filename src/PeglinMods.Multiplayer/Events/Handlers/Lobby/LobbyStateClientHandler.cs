namespace PeglinMods.Multiplayer.Events.Handlers.Lobby;

using PeglinMods.Multiplayer.Events.Network.Lobby;
using PeglinMods.Multiplayer.UI;

public sealed class LobbyStateClientHandler : IClientHandler<LobbyStateEvent>
{
    public void Handle(LobbyStateEvent networkEvent)
    {
        LobbyUI.ApplyLobbyState(networkEvent);
    }
}
