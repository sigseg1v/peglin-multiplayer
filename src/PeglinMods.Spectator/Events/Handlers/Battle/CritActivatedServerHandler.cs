namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class CritActivatedServerHandler : IServerHandler<CritActivatedEvent>
{
    public CritActivatedEvent Handle(CritActivatedEvent networkEvent) => networkEvent;
}
