namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class CritActivatedServerHandler : IServerHandler<CritActivatedEvent>
{
    public CritActivatedEvent Handle(CritActivatedEvent networkEvent) => networkEvent;
}
