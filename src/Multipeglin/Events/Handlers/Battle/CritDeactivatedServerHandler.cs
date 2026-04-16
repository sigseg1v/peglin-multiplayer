namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class CritDeactivatedServerHandler : IServerHandler<CritDeactivatedEvent>
{
    public CritDeactivatedEvent Handle(CritDeactivatedEvent networkEvent) => networkEvent;
}
