using PeglinMods.Spectator.Events.Network;

namespace PeglinMods.Spectator.Events.Handlers;

public sealed class HandshakeServerHandler : IServerHandler<HandshakeEvent>
{
    public HandshakeEvent Handle(HandshakeEvent networkEvent) => networkEvent;
}
