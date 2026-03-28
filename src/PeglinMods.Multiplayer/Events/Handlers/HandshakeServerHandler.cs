using PeglinMods.Multiplayer.Events.Network;

namespace PeglinMods.Multiplayer.Events.Handlers;

public sealed class HandshakeServerHandler : IServerHandler<HandshakeEvent>
{
    public HandshakeEvent Handle(HandshakeEvent networkEvent) => networkEvent;
}
