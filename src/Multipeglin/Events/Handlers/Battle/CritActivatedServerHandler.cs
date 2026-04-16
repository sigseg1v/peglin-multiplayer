namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class CritActivatedServerHandler : IServerHandler<CritActivatedEvent>
{
    public CritActivatedEvent Handle(CritActivatedEvent networkEvent) => networkEvent;
}
