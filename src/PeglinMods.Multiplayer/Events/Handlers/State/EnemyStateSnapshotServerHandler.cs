using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class EnemyStateSnapshotServerHandler : IServerHandler<EnemyStateSnapshot>
{
    public EnemyStateSnapshot Handle(EnemyStateSnapshot e) => e;
}
