namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class BallPositionServerHandler : IServerHandler<BallPositionEvent>
{
    public BallPositionEvent Handle(BallPositionEvent networkEvent) => networkEvent;
}
