namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class MultiballDestroyedServerHandler : IServerHandler<MultiballDestroyedEvent>
{
    public MultiballDestroyedEvent Handle(MultiballDestroyedEvent networkEvent) => networkEvent;
}
