using PeglinMods.Multiplayer.Events.Network;

namespace PeglinMods.Multiplayer.Events.Handlers;

public sealed class DisconnectServerHandler : IServerHandler<DisconnectEvent>
{
    public DisconnectEvent Handle(DisconnectEvent networkEvent) => networkEvent;
}
