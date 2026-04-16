namespace Multipeglin.Events.Handlers.Health;

using Multipeglin.Events.Network.Health;

public sealed class MaxHealthChangedServerHandler : IServerHandler<MaxHealthChangedEvent>
{
    public MaxHealthChangedEvent Handle(MaxHealthChangedEvent networkEvent) => networkEvent;
}
