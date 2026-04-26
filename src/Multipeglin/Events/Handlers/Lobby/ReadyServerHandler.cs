using Multipeglin.Events.Network.Lobby;

namespace Multipeglin.Events.Handlers.Lobby;

public sealed class ReadyServerHandler : IServerHandler<ReadyEvent>
{
    public ReadyEvent Handle(ReadyEvent networkEvent) => null; // Don't rebroadcast
}
