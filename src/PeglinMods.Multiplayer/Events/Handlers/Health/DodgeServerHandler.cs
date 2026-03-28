namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class DodgeServerHandler : IServerHandler<DodgeEvent>
{
    public DodgeEvent Handle(DodgeEvent networkEvent) => networkEvent;
}
