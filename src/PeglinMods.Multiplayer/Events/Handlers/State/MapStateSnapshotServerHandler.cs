using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class MapStateSnapshotServerHandler : IServerHandler<MapStateSnapshot>
{
    public MapStateSnapshot Handle(MapStateSnapshot e) => e;
}
