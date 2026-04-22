using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class BallStateSnapshotServerHandler : IServerHandler<BallStateSnapshot>
{
    public BallStateSnapshot Handle(BallStateSnapshot e) => e;
}
