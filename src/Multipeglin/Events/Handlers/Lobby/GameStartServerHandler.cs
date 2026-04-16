namespace Multipeglin.Events.Handlers.Lobby;

using Multipeglin.Events.Network.Lobby;

public sealed class GameStartServerHandler : IServerHandler<GameStartEvent>
{
    public GameStartEvent Handle(GameStartEvent networkEvent) => networkEvent;
}
