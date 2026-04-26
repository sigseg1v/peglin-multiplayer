
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;
public sealed class CritActivatedServerHandler : IServerHandler<CritActivatedEvent>
{
    public CritActivatedEvent Handle(CritActivatedEvent networkEvent) => networkEvent;
}
