using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class PlayerStateSnapshotServerHandler : IServerHandler<PlayerStateSnapshot>
{
    public PlayerStateSnapshot Handle(PlayerStateSnapshot e) => e;
}
