namespace PeglinMods.Multiplayer.Events.Handlers.Lobby;

using PeglinMods.Multiplayer.Events.Network.Lobby;

public sealed class ReadyServerHandler : IServerHandler<ReadyEvent>
{
    public ReadyEvent Handle(ReadyEvent networkEvent) => null; // Don't rebroadcast
}
