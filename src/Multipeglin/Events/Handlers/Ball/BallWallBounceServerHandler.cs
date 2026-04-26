using Multipeglin.Events.Network.Ball;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class BallWallBounceServerHandler : IServerHandler<BallWallBounceEvent>
{
    public BallWallBounceEvent Handle(BallWallBounceEvent networkEvent) => networkEvent;
}
