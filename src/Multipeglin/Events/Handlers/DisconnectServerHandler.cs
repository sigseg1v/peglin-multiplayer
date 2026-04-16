using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

public sealed class DisconnectServerHandler : IServerHandler<DisconnectEvent>
{
    public DisconnectEvent Handle(DisconnectEvent networkEvent) => networkEvent;
}
