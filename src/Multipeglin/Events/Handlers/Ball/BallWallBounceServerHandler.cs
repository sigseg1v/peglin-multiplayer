namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class BallWallBounceServerHandler : IServerHandler<BallWallBounceEvent>
{
    public BallWallBounceEvent Handle(BallWallBounceEvent networkEvent) => networkEvent;
}
