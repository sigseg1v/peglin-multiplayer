using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class PlayerStateSnapshotServerHandler : IServerHandler<PlayerStateSnapshot>
{
    public PlayerStateSnapshot Handle(PlayerStateSnapshot e) => e;
}
