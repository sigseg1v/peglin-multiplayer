namespace PeglinMods.Spectator.Events.Handlers.Ball;

using PeglinMods.Spectator.Events.Network.Ball;

public sealed class BallWallBounceServerHandler : IServerHandler<BallWallBounceEvent>
{
    public BallWallBounceEvent Handle(BallWallBounceEvent networkEvent) => networkEvent;
}
