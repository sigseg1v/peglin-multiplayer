using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class RelicStateSnapshotServerHandler : IServerHandler<RelicStateSnapshot>
{
    public RelicStateSnapshot Handle(RelicStateSnapshot e) => e;
}
