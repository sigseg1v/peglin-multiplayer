namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class CritDeactivatedServerHandler : IServerHandler<CritDeactivatedEvent>
{
    public CritDeactivatedEvent Handle(CritDeactivatedEvent networkEvent) => networkEvent;
}
