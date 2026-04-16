using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class MapStateSnapshotServerHandler : IServerHandler<MapStateSnapshot>
{
    public MapStateSnapshot Handle(MapStateSnapshot e) => e;
}
