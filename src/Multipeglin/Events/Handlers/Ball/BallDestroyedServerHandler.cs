namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class BallDestroyedServerHandler : IServerHandler<BallDestroyedEvent>
{
    public BallDestroyedEvent Handle(BallDestroyedEvent networkEvent) => networkEvent;
}
