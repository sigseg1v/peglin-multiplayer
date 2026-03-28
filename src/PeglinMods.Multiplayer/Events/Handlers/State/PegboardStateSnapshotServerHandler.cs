using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class PegboardStateSnapshotServerHandler : IServerHandler<PegboardStateSnapshot>
{
    public PegboardStateSnapshot Handle(PegboardStateSnapshot e) => e;
}
