
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;
public sealed class CritDeactivatedServerHandler : IServerHandler<CritDeactivatedEvent>
{
    public CritDeactivatedEvent Handle(CritDeactivatedEvent networkEvent) => networkEvent;
}
