namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using PeglinMods.Multiplayer.Events.Network.Ball;

public sealed class BallWallBounceServerHandler : IServerHandler<BallWallBounceEvent>
{
    public BallWallBounceEvent Handle(BallWallBounceEvent networkEvent) => networkEvent;
}
