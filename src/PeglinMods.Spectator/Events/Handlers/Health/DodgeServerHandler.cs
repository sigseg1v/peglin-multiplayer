namespace PeglinMods.Spectator.Events.Handlers.Health;

using PeglinMods.Spectator.Events.Network.Health;

public sealed class DodgeServerHandler : IServerHandler<DodgeEvent>
{
    public DodgeEvent Handle(DodgeEvent networkEvent) => networkEvent;
}
