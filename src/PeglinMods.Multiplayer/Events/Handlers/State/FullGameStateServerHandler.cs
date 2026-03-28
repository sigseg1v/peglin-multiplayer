using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class FullGameStateServerHandler : IServerHandler<FullGameStateSnapshot>
{
    public FullGameStateSnapshot Handle(FullGameStateSnapshot e) => e;
}
