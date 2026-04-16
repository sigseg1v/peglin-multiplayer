using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class EnemyStateSnapshotServerHandler : IServerHandler<EnemyStateSnapshot>
{
    public EnemyStateSnapshot Handle(EnemyStateSnapshot e) => e;
}
