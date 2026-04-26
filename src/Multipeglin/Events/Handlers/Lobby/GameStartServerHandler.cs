
using Multipeglin.Events.Network.Lobby;

namespace Multipeglin.Events.Handlers.Lobby;
public sealed class GameStartServerHandler : IServerHandler<GameStartEvent>
{
    public GameStartEvent Handle(GameStartEvent networkEvent) => networkEvent;
}
