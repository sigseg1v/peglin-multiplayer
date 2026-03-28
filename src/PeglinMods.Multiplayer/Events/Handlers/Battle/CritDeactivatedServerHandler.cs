namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class CritDeactivatedServerHandler : IServerHandler<CritDeactivatedEvent>
{
    public CritDeactivatedEvent Handle(CritDeactivatedEvent networkEvent) => networkEvent;
}
