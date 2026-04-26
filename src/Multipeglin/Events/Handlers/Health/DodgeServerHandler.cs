using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class DodgeServerHandler : IServerHandler<DodgeEvent>
{
    public DodgeEvent Handle(DodgeEvent networkEvent) => networkEvent;
}
