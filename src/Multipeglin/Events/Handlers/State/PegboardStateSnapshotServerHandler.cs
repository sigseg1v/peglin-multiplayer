using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class PegboardStateSnapshotServerHandler : IServerHandler<PegboardStateSnapshot>
{
    public PegboardStateSnapshot Handle(PegboardStateSnapshot e) => e;
}
