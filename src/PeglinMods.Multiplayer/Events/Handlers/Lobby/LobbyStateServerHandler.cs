namespace PeglinMods.Multiplayer.Events.Handlers.Lobby;

using PeglinMods.Multiplayer.Events.Network.Lobby;

public sealed class LobbyStateServerHandler : IServerHandler<LobbyStateEvent>
{
    public LobbyStateEvent Handle(LobbyStateEvent networkEvent) => networkEvent;
}
