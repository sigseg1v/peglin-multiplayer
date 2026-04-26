
using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;
public sealed class MaxHealthChangedServerHandler : IServerHandler<MaxHealthChangedEvent>
{
    public MaxHealthChangedEvent Handle(MaxHealthChangedEvent networkEvent) => networkEvent;
}
