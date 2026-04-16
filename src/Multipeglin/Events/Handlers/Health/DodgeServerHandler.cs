namespace Multipeglin.Events.Handlers.Health;

using Multipeglin.Events.Network.Health;

public sealed class DodgeServerHandler : IServerHandler<DodgeEvent>
{
    public DodgeEvent Handle(DodgeEvent networkEvent) => networkEvent;
}
