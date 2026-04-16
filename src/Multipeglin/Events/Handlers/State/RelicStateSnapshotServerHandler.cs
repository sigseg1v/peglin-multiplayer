using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class RelicStateSnapshotServerHandler : IServerHandler<RelicStateSnapshot>
{
    public RelicStateSnapshot Handle(RelicStateSnapshot e) => e;
}
