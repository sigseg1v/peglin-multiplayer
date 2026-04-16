using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

public sealed class HandshakeServerHandler : IServerHandler<HandshakeEvent>
{
    public HandshakeEvent Handle(HandshakeEvent networkEvent) => networkEvent;
}
