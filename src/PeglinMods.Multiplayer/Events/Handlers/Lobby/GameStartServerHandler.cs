namespace PeglinMods.Multiplayer.Events.Handlers.Lobby;

using PeglinMods.Multiplayer.Events.Network.Lobby;

public sealed class GameStartServerHandler : IServerHandler<GameStartEvent>
{
    public GameStartEvent Handle(GameStartEvent networkEvent) => networkEvent;
}
