namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using PeglinMods.Multiplayer.Events.Network.Ball;

public sealed class BallPositionServerHandler : IServerHandler<BallPositionEvent>
{
    public BallPositionEvent Handle(BallPositionEvent networkEvent) => networkEvent;
}
