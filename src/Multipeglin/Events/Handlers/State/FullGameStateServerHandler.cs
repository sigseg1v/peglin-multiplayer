using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class FullGameStateServerHandler : IServerHandler<FullGameStateSnapshot>
{
    public FullGameStateSnapshot Handle(FullGameStateSnapshot e) => e;
}
