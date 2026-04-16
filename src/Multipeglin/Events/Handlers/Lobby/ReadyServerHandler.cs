namespace Multipeglin.Events.Handlers.Lobby;

using Multipeglin.Events.Network.Lobby;

public sealed class ReadyServerHandler : IServerHandler<ReadyEvent>
{
    public ReadyEvent Handle(ReadyEvent networkEvent) => null; // Don't rebroadcast
}
